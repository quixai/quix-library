﻿namespace Quix.Snowflake.Infrastructure.TimeSeries.Models
{
    /// <summary>
    /// Snowflake database connection configuration.
    /// </summary>
    public class SnowflakeConnectionConfiguration
    {
        public string Locator { get; set; }
        public string Region { get; set; }
        public string User { get; set; }
        public string Password { get; set; }
        public string Database { get; set; }
        
        public string ConnectionString
        {
            get
            {
                return $"account={Locator}.{Region};user={User};password={Password};db={Database}";
            }
        }
    }
}
