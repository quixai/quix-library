using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace Quix.SqlServer.Writer.Configuration
{
    [ExcludeFromCodeCoverage]
    public class BrokerConfiguration
    {
        /// <summary>
        /// The topic name used for display purposes
        /// </summary>
        public string TopicName { get; set; }

        /// <summary>
        /// When data is read from the broker, commit should occur after reading this amount of messages. (Or another condition occurs)
        /// </summary>
        public int? CommitAfterEveryCount { get; set; }
        
        /// <summary>
        /// When data is first read from broker (either from start or after last commit), wait this amount of milliseconds then do a commit. (or another condition occurs) 
        /// </summary>
        public int? CommitAfterMs { get; set; }

        /// <summary>
        /// Extra properties for Kafka broker configuration
        /// </summary>
        public Dictionary<string, string> Properties { get; set; }
    }

}
