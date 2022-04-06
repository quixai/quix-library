using Microsoft.Extensions.Hosting;
using Quix.SqlServer.Infrastructure.TimeSeries.Repositories;

namespace Quix.SqlServer.Writer
{
    public class Program
    {
        public static void Main(string[] args)
        {
            //SqlServerWriteRepository.foo();
            
            var built = CreateHostBuilder(args).Build();
            Startup.AfterBuild(built.Services);
            built.Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, builder) => Startup.ConfigureAppConfiguration(context, builder, args))
                .ConfigureServices(Startup.ConfigureServices)
                .ConfigureLogging(Startup.ConfigureLogging);
    }
}