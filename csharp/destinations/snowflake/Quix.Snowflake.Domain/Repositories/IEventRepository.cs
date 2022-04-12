using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Common;
using Quix.Snowflake.Domain.Models;

namespace Quix.Snowflake.Domain.Repositories
{
    /// <summary>
    /// Repository for persisting new <see cref="TelemetryEvent"/> and reading existing
    /// </summary>
    public interface IEventRepository
    {
        /// <summary>
        /// Gets all events as queryable for further filtering
        /// </summary>
        /// <returns>Queryable telemetry events</returns>
        Task<IList<TelemetryEvent>> Get(FilterDefinition<TelemetryEvent> filter);
        
        /// <summary>
        /// Bulk write capable of updating/inserting/deleting multiple things at a time
        /// </summary>
        /// <param name="requests">The request to execute</param>
        Task BulkWrite(IEnumerable<WriteModel<TelemetryEvent>> requests);
    }
    
    public class NullEventRepository : IEventRepository
    {
        public Task BulkWrite(IEnumerable<WriteModel<TelemetryEvent>> insertRequests)
        {
            return Task.CompletedTask;
        }

        public Task<IList<TelemetryEvent>> Get(FilterDefinition<TelemetryEvent> filter)
        {
            return Task.FromResult(new List<TelemetryEvent>() as IList<TelemetryEvent>);
        }
    }
}