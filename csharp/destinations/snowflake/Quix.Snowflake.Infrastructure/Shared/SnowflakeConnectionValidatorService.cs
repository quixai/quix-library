using System;
using System.Data;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Snowflake.Data.Client;

namespace Quix.Snowflake.Infrastructure.Shared
{
    public class SnowflakeConnectionValidatorService
    {
        private readonly SnowflakeDbConnection connection;
        private readonly ILogger<SnowflakeConnectionValidatorService> logger;

        public SnowflakeConnectionValidatorService(SnowflakeDbConnection connection, ILoggerFactory loggerFactory)
        {
            this.logger = loggerFactory.CreateLogger<SnowflakeConnectionValidatorService>();
            this.connection = connection;
        }

        public void Validate()
        {
            return;
            this.logger.LogInformation($"Connecting to Snowflake database {connection.Database}");
            var openTask = Task.Run(() => { connection.Open(); });
            Task.WaitAny(openTask, Task.Delay(10000));
            if (openTask.IsFaulted) throw openTask.Exception ?? new Exception("Connection failed");
            if (!openTask.IsCompleted) throw new TimeoutException("Connection timed out"); 
            this.logger.LogInformation($"Connected to Snowflake database {connection.Database}");
            this.connection.ExecuteSnowflakeStatement("SELECT 1 = 1");
        }
    }
}