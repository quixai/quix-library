using System;
using System.Data;
using System.IO;
using System.Net.Http;
using System.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quix.SqlServer.Application.Metadata;
using Quix.SqlServer.Application.Streaming;
using Quix.SqlServer.Application.TimeSeries;
using Quix.SqlServer.Domain.Models;
using Quix.SqlServer.Domain.Repositories;
using Quix.SqlServer.Domain.TimeSeries.Repositories;
using Quix.SqlServer.Infrastructure.Metadata;
using Quix.SqlServer.Infrastructure.Shared;
using Quix.SqlServer.Infrastructure.TimeSeries.Models;
using Quix.SqlServer.Infrastructure.TimeSeries.Repositories;
using Quix.SqlServer.Writer.Configuration;
using Quix.SqlServer.Writer.Helpers;
using Serilog;

namespace Quix.SqlServer.Writer
{
    public class Startup
    {
        
        private static readonly HttpClient HttpClient = new HttpClient();
        private const int GcTimerInterval = 1000;
        private static readonly Timer GcTimer = new Timer(GcTimerCallback, null, GcTimerInterval, Timeout.Infinite);
        
        private static void GcTimerCallback(object state)
        {
            // Because we're hosting the app in Kubernetes, reporting the memory closer to what it actually is
            // is better than what is claimed.
            GC.Collect();
            GcTimer.Change(GcTimerInterval, Timeout.Infinite);
        }
        
        internal static void ConfigureServices(HostBuilderContext context, IServiceCollection services)
        {
            services.AddLazyResolution();
            
            ConfigureAppSettings(context, services);
            ConfigureApplication(context, services);

            // Configure Components from this proj
            services.AddHostedService<SqlServer>();
            
            var gcMemoryInfo = GC.GetGCMemoryInfo();
            var maxTotalMemory = (int)(gcMemoryInfo.TotalAvailableMemoryBytes / 1024 / 1024);
            var maxMemory = context.Configuration.GetValue("Quix:Deployment:Limits:Memory", 2000);
            var maxMemoryToUse = Math.Min(maxMemory, maxTotalMemory);
            var memoryLimitPercentage = 100 - context.Configuration.GetValue("MemoryPercentLeftForProcess", 50);
            Console.WriteLine($"Max memory available: {maxTotalMemory} MB, configured: {maxMemory} MB, Using: {maxMemoryToUse} MB, Used for Messages: {memoryLimitPercentage * maxMemoryToUse / 100} MB");
            services.AddScoped(s => new MemoryLimiterComponent(s.GetService<ILogger<MemoryLimiterComponent>>(), maxMemoryToUse, memoryLimitPercentage));
            
            services.AddSingleton((s) => HttpClient); // To allow reuse of httpclient which prevents port exhaustion
        }
        
        private static void ConfigureAppSettings(HostBuilderContext context, IServiceCollection services)
        {
            var wsId = context.Configuration.GetValue<string>("Quix:Workspace:Id");
            services.AddSingleton(new WorkspaceId(wsId));
            
            var topicName = context.Configuration.GetValue<string>("Broker:TopicName");
            services.AddSingleton(new TopicName(topicName));

            var SqlServer = new SqlServerConnectionConfiguration();
            context.Configuration.Bind("SqlServer", SqlServer);
            services.AddTransient(s => SqlServer);

            var brokerSettings = new BrokerConfiguration();
            context.Configuration.Bind("Broker", brokerSettings);
            services.AddSingleton(s => brokerSettings);
        }

        private static void ConfigureApplication(HostBuilderContext context, IServiceCollection services)
        {
            // SqlServer
            services.AddScoped<SqlServerConnectionValidatorService>();
            SqlServerSchemaRegistry.Register();
            services.AddSingleton(sc =>
            {
                var config = sc.GetRequiredService<SqlServerConnectionConfiguration>();
                //var conn = new SqlServerDbConnection();
                var conn = new SqlConnection();
                conn.ConnectionString = config.ConnectionString;
                return conn;
            });
            services.AddSingleton<IDbConnection>(sc => sc.GetRequiredService<SqlConnection>()); // using IDbConnection at some places for mocking purposes
            // services.AddSingleton<IDbConnection>(sc => sc.GetRequiredService<SqlServerDbConnection>()); // using IDbConnection at some places for mocking purposes
            
            // Stream context
            services.AddScoped<StreamPersistingComponent>();

            // TimeSeries Context
            services.AddSingleton<ITimeSeriesWriteRepository, TimeSeriesWriteRepository>();

            services.AddSingleton<QuixConfigHelper>();
            services.AddSingleton((sp) => new TopicId(sp.GetRequiredService<QuixConfigHelper>().GetConfiguration().GetAwaiter().GetResult().topicId));
            
            var batchSize = context.Configuration.GetValue<int>("SqlServer:BatchSize");
            services.AddSingleton<ITimeSeriesBufferedPersistingService, TimeSeriesBufferedPersistingService>(s =>
            {
                return new TimeSeriesBufferedPersistingService(
                    s.GetRequiredService<ILogger<TimeSeriesBufferedPersistingService>>(),
                    s.GetRequiredService<ITimeSeriesWriteRepository>(),
                    s.GetRequiredService<TopicId>(),
                    batchSize);
            });
            
            // Metadata Context
            services.AddSingleton<IStreamRepository, StreamRepository>();
            services.AddSingleton<IParameterRepository, ParameterRepository>();
            services.AddSingleton<IParameterGroupRepository, ParameterGroupRepository>();
            services.AddSingleton<IEventRepository, EventRepository>();
            services.AddSingleton<IEventGroupRepository, EventGroupRepository>();
            services.AddSingleton<IParameterPersistingService, ParameterPersistingService>();
            services.AddSingleton<IEventPersistingService, EventPersistingService>();
            services.AddSingleton<IMetadataBufferedPersistingService, MetadataBufferedPersistingService>();
            var idleTimeMs = Math.Max(60000, context.Configuration.GetValue<int>("StreamIdleTimeMs"));
            services.AddSingleton(y=> new StreamIdleTime(idleTimeMs));
        }
        
        internal static void ConfigureLogging(HostBuilderContext context, ILoggingBuilder builder)
        {
            builder.ClearProviders();
            
            // configure Logging with Serilog
            Log.Logger = new Serilog.LoggerConfiguration()
                .ReadFrom.Configuration(context.Configuration)
                .Enrich.FromLogContext()
                .CreateLogger();

            SerilogWindowsConsole.EnableVirtualTerminalProcessing();

            builder.AddSerilog(dispose: true);
        }
        
        internal static void ConfigureAppConfiguration(HostBuilderContext context, IConfigurationBuilder builder, string[] args)
        {
            builder.SetBasePath(Directory.GetCurrentDirectory());
            builder.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
            builder.AddEnvironmentVariables();
            builder.AddCommandLine(args);
        }

        public static void AfterBuild(IServiceProvider serviceProvider)
        {
            try
            {
                var conn = serviceProvider.GetRequiredService<SqlServerConnectionValidatorService>();
                conn.Validate();
                Console.WriteLine("CONNECTED!");
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR: {0}", ex.Message);
                Environment.Exit(-1);
            }

        }
    }
}