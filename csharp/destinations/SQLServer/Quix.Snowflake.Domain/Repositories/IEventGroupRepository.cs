using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Models;

namespace Quix.Snowflake.Domain.Repositories
{
    /// <summary>
    /// Repository for persisting new <see cref="TelemetryEventGroup"/> and reading existing
    /// </summary>
    public interface IEventGroupRepository
    {
        /// <summary>
        /// Add <see cref="TelemetryEventGroup"/>s to the repository
        /// </summary>
        /// <param name="eventGroups">The event groups to add to the database</param>
        Task Save(ICollection<TelemetryEventGroup> eventGroups);

        /// <summary>
        /// Gets all event groups as queryable for further filtering
        /// </summary>
        /// <returns>Queryable telemetry event groups</returns>
        IQueryable<TelemetryEventGroup> GetAll();
        
        /// <summary>
        /// Deletes the specified event groups for the stream
        /// </summary>
        /// <param name="streamId">The stream to delete from</param>
        /// <param name="groupPaths">The group at the paths to delete</param>
        /// <returns>The number of groups deleted</returns>
        Task<long> Delete(string streamId, ICollection<string> groupPaths);

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
        Task BulkWrite(IEnumerable<TelemetryEventGroup> requests);
    }
}