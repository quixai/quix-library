using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Common;
using Quix.Snowflake.Domain.Models;

namespace Quix.Snowflake.Domain.Repositories
{
    /// <summary>
    /// Repository for persisting new <see cref="TelemetryParameter"/> and reading existing
    /// </summary>
    public interface IParameterRepository
    {
        Task<IList<TelemetryParameter>> Get(FilterDefinition<TelemetryParameter> filter);
        
        /// <summary>
        /// Bulk write capable of updating/inserting/deleting multiple things at a time
        /// </summary>
        /// <param name="requests">The request to execute</param>
        Task BulkWrite(IEnumerable<WriteModel<TelemetryParameter>> requests);
    }
    
    public class NullParameterRepository : IParameterRepository
    {
        public Task BulkWrite(IEnumerable<WriteModel<TelemetryParameter>> insertRequests)
        {
            return Task.CompletedTask;
        }

        public Task<IList<TelemetryParameter>> Get(FilterDefinition<TelemetryParameter> filter)
        {
            return Task.FromResult(new List<TelemetryParameter>() as IList<TelemetryParameter>);
        }
    }
}
