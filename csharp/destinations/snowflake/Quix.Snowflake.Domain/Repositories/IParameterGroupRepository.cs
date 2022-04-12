using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Common;
using Quix.Snowflake.Domain.Models;

namespace Quix.Snowflake.Domain.Repositories
{
    /// <summary>
    /// Repository for persisting new <see cref="TelemetryParameterGroup"/> and reading existing
    /// </summary>
    public interface IParameterGroupRepository
    {
        /// <summary>
        /// Gets all parameter groups as queryable for further filtering
        /// </summary>
        /// <returns>Queryable telemetry parameter groups</returns>
        Task<IList<TelemetryParameterGroup>> Get(FilterDefinition<TelemetryParameterGroup> filter);
        
        /// <summary>
        /// Bulk write capable of updating/inserting/deleting multiple things at a time
        /// </summary>
        /// <param name="requests">The request to execute</param>
        Task BulkWrite(IEnumerable<WriteModel<TelemetryParameterGroup>> requests);
    }
    
    public class NullParameterGroupRepository : IParameterGroupRepository
    {
        public Task BulkWrite(IEnumerable<WriteModel<TelemetryParameterGroup>> insertRequests)
        {
            return Task.CompletedTask;
        }

        public Task<IList<TelemetryParameterGroup>> Get(FilterDefinition<TelemetryParameterGroup> filter)
        {
            return Task.FromResult(new List<TelemetryParameterGroup>() as IList<TelemetryParameterGroup>);
        }
    }
}
