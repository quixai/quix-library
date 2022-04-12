using System;
using System.Data;
using System.IO;
using System.Net.Http;
using System.Threading;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Quix.Snowflake.Application.Metadata;
using Quix.Snowflake.Application.Streaming;
using Quix.Snowflake.Application.TimeSeries;
using Quix.Snowflake.Domain.Common;
using Quix.Snowflake.Domain.Models;
using Quix.Snowflake.Domain.Repositories;
using Quix.Snowflake.Domain.TimeSeries.Repositories;
using Quix.Snowflake.Infrastructure.Metadata;
using Quix.Snowflake.Infrastructure.Shared;
using Quix.Snowflake.Infrastructure.TimeSeries.Models;
using Quix.Snowflake.Infrastructure.TimeSeries.Repositories;
using Quix.Snowflake.Writer.Configuration;
using Quix.Snowflake.Writer.Helpers;
using Serilog;
using Snowflake.Data.Client;

namespace Quix.Snowflake.Writer
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
            services.AddHostedService<Worker>();
            
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

            var snowflake = new SnowflakeConnectionConfiguration();
            context.Configuration.Bind("Snowflake", snowflake);
            services.AddTransient(s => snowflake);

            var brokerSettings = new BrokerConfiguration();
            context.Configuration.Bind("Broker", brokerSettings);
            services.AddSingleton(s => brokerSettings);
        }

        private static void ConfigureApplication(HostBuilderContext context, IServiceCollection services)
        {
            // Snowflake
            services.AddScoped<SnowflakeConnectionValidatorService>();
            SnowflakeSchemaRegistry.Register();
            services.AddSingleton(sc =>
            {
                var config = sc.GetRequiredService<SnowflakeConnectionConfiguration>();
                var conn = new SnowflakeDbConnection();
                conn.ConnectionString = config.ConnectionString;
                return conn;
            });
            services.AddSingleton<IDbConnection>(sc => Substitute.For<IDbConnection>()); // using IDbConnection at some places for mocking purposes
            
            // Stream context
            services.AddScoped<StreamPersistingComponent>();

            // TimeSeries Context
            services.AddSingleton<ITimeSeriesWriteRepository, NullTimeSeriesWriteRepository>();

            services.AddSingleton<QuixConfigHelper>();
            services.AddSingleton((sp) => new TopicId(sp.GetRequiredService<QuixConfigHelper>().GetConfiguration().GetAwaiter().GetResult().topicId));
            
            var batchSize = context.Configuration.GetValue<int>("Snowflake:BatchSize");
            services.AddSingleton<ITimeSeriesBufferedPersistingService, TimeSeriesBufferedPersistingService>(s =>
            {
                return new TimeSeriesBufferedPersistingService(
                    s.GetRequiredService<ILogger<TimeSeriesBufferedPersistingService>>(),
                    s.GetRequiredService<ITimeSeriesWriteRepository>(),
                    s.GetRequiredService<TopicId>(),
                    batchSize);
            });
            
            // Metadata Context
            services.AddSingleton<IStreamRepository, NullStreamRepository>();
            services.AddSingleton<IParameterRepository, NullParameterRepository>();
            services.AddSingleton<IParameterGroupRepository, NullParameterGroupRepository>();
            services.AddSingleton<IEventRepository, NullEventRepository>();
            services.AddSingleton<IEventGroupRepository, NullEventGroupRepository>();
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
                var conn = serviceProvider.GetRequiredService<SnowflakeConnectionValidatorService>();
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