using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Models;

namespace Quix.Snowflake.Domain.Repositories
{
    /// <summary>
    /// Repository for persisting new <see cref="TelemetryEvent"/> and reading existing
    /// </summary>
    public interface IEventRepository
    {
        /// <summary>
        /// Save <see cref="TelemetryEvent"/>s to the repository
        /// </summary>
        /// <param name="events">The events to save</param>
        Task Save(ICollection<TelemetryEvent> events);
        
        /// <summary>
        /// Gets all events as queryable for further filtering
        /// </summary>
        /// <returns>Queryable telemetry events</returns>
        IQueryable<TelemetryEvent> GetAll();

        /// <summary>
        /// Deletes the specified events for the stream
        /// </summary>
        /// <param name="streamId">The stream to delete from</param>
        /// <param name="eventIds">The events to delete</param>
        /// <returns>The number of events deleted</returns>
        Task<long> Delete(string streamId, ICollection<string> eventIds);
        
        /// <summary>
        /// Delete all events of a stream
        /// </summary>
        /// <param name="streamId">The id of the stream to delete the events for</param>
        /// <returns>The amount of events deleted</returns>
        Task<long> DeleteAll(string streamId);
        
        /// <summary>
        /// Bulk write capable of updating/inserting/deleting multiple things at a time
        /// </summary>
        /// <param name="requests">The request to execute</param>
        Task BulkWrite(IEnumerable<TelemetryEvent> requests);
    }
}