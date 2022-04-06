using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.SqlServer.Domain.Models;

namespace Quix.SqlServer.Domain.Repositories
{
    /// <summary>
    /// Repository for persisting new <see cref="TelemetryStream"/> and reading existing
    /// </summary>
    public interface IStreamRepository
    {
        /// <summary>
        /// Add a <see cref="TelemetryStream"/> to the repository
        /// </summary>
        /// <param name="stream"></param>
        Task Save(TelemetryStream stream);

        //todo implement update
        /// <summary>
        /// Updates the specified stream
        /// </summary>
        /// <param name="streamId">The id of the stream to update</param>
        /// <param name="update"></param>
        Task Update(string streamId, TelemetryStream update);

        /// <summary>
        /// Bulk write capable of updating/inserting/deleting multiple things at a time
        /// </summary>
        /// <param name="insertRequests">The request to execute</param>
        Task BulkWrite(IEnumerable<TelemetryStream> insertRequests);
        
        /// <summary>
        /// Deletes a stream by streamId
        /// </summary>
        /// <param name="streamId">StreamId of the stream to delete</param>
        Task Delete(string streamId);

        /// <summary>
        /// Marks the provided stream ids for deletion
        /// </summary>
        /// <param name="streamIds">The stream ids to mark for deletion</param>
        /// <param name="hardDelete">Hard or Soft delete the streams</param>
        /// <returns>awaitable task</returns>
        Task<long> MarkDeleted(ICollection<string> streamIds, bool hardDelete);
        
        /// <summary>
        /// Restores the provided stream ids to non deleted
        /// </summary>
        /// <param name="streamIds">The stream ids to mark for deletion</param>
        /// <returns>awaitable task</returns>
        Task<long> Restore(ICollection<string> streamIds);

        /// <summary>
        /// Gets all streams as queryable for further filtering
        /// </summary>
        /// <returns>Queryable telemetry streams</returns>
        IQueryable<TelemetryStream> GetAll();
    }
}
