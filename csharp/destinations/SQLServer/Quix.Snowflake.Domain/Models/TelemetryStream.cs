using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Quix.Snowflake.Domain.Models
{
    [DebuggerDisplay("{StreamId}, {Name}")]
    public class TelemetryStream
    {
        public TelemetryStream()
        {
            this.Children = new List<TelemetryStream>();
        }

        public TelemetryStream(string streamId)
        {
            this.StreamId = streamId ?? throw new ArgumentNullException(nameof(streamId));
        }

        public string StreamId { get; private set; }

        public string Name { get; set; }
        
        //[BsonIgnore]
        public List<TelemetryStream> Children { get; set; }

        /// <summary>
        /// The time of first stream save.
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// The last time the stream was updated
        /// </summary>
        public DateTime LastUpdate { get; set; } = DateTime.UtcNow;
        
        /// <summary>
        /// Time of the recording. Optional.
        /// </summary>
        // [BsonIgnoreIfNull]
        public DateTime? TimeOfRecording { get; set; }
        
        /// <summary>
        /// The earliest data within the session in unix nanoseconds
        /// </summary>
        // [BsonIgnoreIfNull]
        public long? Start { get; set; }

        /// <summary>
        /// The last data within the session in unix nanoseconds
        /// </summary>
        // [BsonIgnoreIfNull]
        public long? End { get; set; }
        
        // [BsonIgnoreIfNull]
        public long? Duration
        {
            get
            {
                if (End == null) return null;
                if (Start == null) return null;
                return End - Start;
            }
            set {} // MongoDB Complains :(
        }

        /// <summary>
        /// State of the stream
        /// </summary>
        // [BsonRepresentation(BsonType.String)]
        public StreamStatus Status { get; set; }

        /// <summary>
        /// Metadata (extra context) for the stream
        /// </summary>
        // [BsonDictionaryOptions(DictionaryRepresentation.ArrayOfDocuments)]
        // [BsonIgnoreIfNull]
        public Dictionary<string,string> Metadata { get; set; }

        /// <summary>
        /// The stream Ids this session is derived from.
        /// </summary>
        // [BsonIgnoreIfNull]
        public List<string> Parents { get; set; }

        /// <summary>
        /// The location of the stream
        /// </summary>
        // [BsonIgnoreIfNull]
        public string Location { get; set; }

        /// <summary>
        /// The topic the stream originates from
        /// </summary>
        // [BsonIgnoreIfNull]
        public string Topic { get; set; }

        /// <summary>
        /// The version of the model
        /// </summary>
        public int? Version { get; set; }

        /// <summary>
        /// The migration State
        /// </summary>
        // [BsonIgnoreIfNull]
        public StreamMigrationState MigrationState { get; set; }
        
        /// <summary>
        /// The time of soft deletion.
        /// </summary>
        // [BsonIgnoreIfNull]
        public DateTime? SoftDeleteAt { get; set; }
    }
    
    

    public class StreamMigrationState
    {
        public DateTime MigrationStarted { get; set; } = DateTime.UtcNow;
        public DateTime MigrationLastTick { get; set; } = DateTime.UtcNow;
    }
}
