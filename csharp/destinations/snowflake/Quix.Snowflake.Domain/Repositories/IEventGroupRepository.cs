using System.Collections.Generic;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Common;
using Quix.Snowflake.Domain.Models;

namespace Quix.Snowflake.Domain.Repositories
{
    /// <summary>
    /// Repository for persisting new <see cref="TelemetryEventGroup"/> and reading existing
    /// </summary>
    public interface IEventGroupRepository
    {
        /// <summary>
        /// Gets all event groups as queryable for further filtering
        /// </summary>
        /// <returns>Queryable telemetry event groups</returns>
        Task<IList<TelemetryEventGroup>> Get(FilterDefinition<TelemetryEventGroup> filter);
        
        /// <summary>
        /// Bulk write capable of updating/inserting/deleting multiple things at a time
        /// </summary>
        /// <param name="requests">The request to execute</param>
        Task BulkWrite(IEnumerable<WriteModel<TelemetryEventGroup>> requests);
    }
    
    public class NullEventGroupRepository : IEventGroupRepository
    {
        public Task BulkWrite(IEnumerable<WriteModel<TelemetryEventGroup>> insertRequests)
        {
            return Task.CompletedTask;
        }

        public Task<IList<TelemetryEventGroup>> Get(FilterDefinition<TelemetryEventGroup> filter)
        {
            return Task.FromResult(new List<TelemetryEventGroup>() as IList<TelemetryEventGroup>);
        }
    }
}