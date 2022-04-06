using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Telemetry.Domain.Metadata.Models;

namespace Quix.Telemetry.Domain.Metadata.Repositories
{
    /// <summary>
    /// Repository for persisting new <see cref="TelemetryParameterGroup"/> and reading existing
    /// </summary>
    public interface IParameterGroupRepository
    {
        /// <summary>
        /// Add <see cref="TelemetryParameterGroup"/>s to the repository
        /// </summary>
        /// <param name="parameterGroups">The parameter groups to add to the database</param>
        Task Save(ICollection<TelemetryParameterGroup> parameterGroups);

        /// <summary>
        /// Gets all parameter groups as queryable for further filtering
        /// </summary>
        /// <returns>Queryable telemetry parameter groups</returns>
        IQueryable<TelemetryParameterGroup> GetAll();

        /// <summary>
        /// Delete all groups of a stream
        /// </summary>
        /// <param name="streamId">The id of the stream to delete the groups for</param>
        /// <returns>The amount of groups deleted</returns>
        Task<long> DeleteAll(string streamId);
        
        /// <summary>
        /// Bulk write capable of updating/inserting/deleting multiple things at a time
        /// </summary>
        /// <param name="requests">The request to execute</param>
        Task BulkWrite(IEnumerable<TelemetryParameterGroup> requests);
    }
}
