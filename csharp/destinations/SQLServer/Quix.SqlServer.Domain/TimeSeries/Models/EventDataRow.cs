using System.Collections.Generic;

namespace Quix.SqlServer.Domain.TimeSeries.Models
{
    /// <summary>
    /// Represents one events row with timestamp and multiple tags and columns.
    /// </summary>
    public class EventDataRow
    {
        public long Timestamp { get; set; }
        
        public string EventId { get; set; }

        public string Value { get; set; }

        public Dictionary<string, string> TagValues { get; set; }
    }
}