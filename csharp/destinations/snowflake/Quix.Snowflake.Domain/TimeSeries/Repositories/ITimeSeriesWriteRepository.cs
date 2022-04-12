using System.Collections.Generic;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.TimeSeries.Models;

namespace Quix.Snowflake.Domain.TimeSeries.Repositories
{
    /// <summary>
    /// Interface to persist Quix.Sdk.Process models into time series database.
    /// </summary>
    public interface ITimeSeriesWriteRepository
    {
        /// <summary>
        /// Write batch of <see cref="ParameterDataRow"/> into database.
        /// </summary>
        Task WriteTelemetryData(string topicId, IEnumerable<KeyValuePair<string, IEnumerable<ParameterDataRowForWrite>>> streamParameterData);

        
        /// <summary>
        /// Write batch of <see cref="ParameterDataRow"/> into database.
        /// </summary>
        Task WriteTelemetryEvent(string topicId, IEnumerable<KeyValuePair<string, IEnumerable<EventDataRow>>> streamEventData);
    }

    public class NullTimeSeriesWriteRepository : ITimeSeriesWriteRepository
    {
        public Task WriteTelemetryData(string topicId, IEnumerable<KeyValuePair<string, IEnumerable<ParameterDataRowForWrite>>> streamParameterData)
        {
            return Task.CompletedTask;
        }

        public Task WriteTelemetryEvent(string topicId, IEnumerable<KeyValuePair<string, IEnumerable<EventDataRow>>> streamEventData)
        {
            return Task.CompletedTask;
        }
    }
}