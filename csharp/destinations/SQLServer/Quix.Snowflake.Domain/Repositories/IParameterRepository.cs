using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Models;

namespace Quix.Snowflake.Domain.Repositories
{
    /// <summary>
    /// Repository for persisting new <see cref="TelemetryParameter"/> and reading existing
    /// </summary>
    public interface IParameterRepository
    {
        /// <summary>
        /// Save <see cref="TelemetryParameter"/>s to the repository
        /// </summary>
        /// <param name="parameters">The parameters to save</param>
        Task Save(ICollection<TelemetryParameter> parameters);

        /// <summary>
        /// Gets all parameters as queryable for further filtering
        /// </summary>
        /// <returns>Queryable telemetry parameters</returns>
        IQueryable<TelemetryParameter> GetAll();

        /// <summary>
        /// Deletes the specified parameters for the stream
        /// </summary>
        /// <param name="streamId">The stream to delete from</param>
        /// <param name="parameterIds">The parameters to delete</param>
        /// <returns>The number of parameters deleted</returns>
        Task<long> Delete(string streamId, ICollection<string> parameterIds);
        
        /// <summary>
        /// Delete all parameters of a stream
        /// </summary>
        /// <param name="streamId">The id of the stream to delete the parameters for</param>
        /// <returns>The amount of parameters deleted</returns>
        Task<long> DeleteAll(string streamId);
        
        /// <summary>
        /// Bulk write capable of updating/inserting/deleting multiple things at a time
        /// </summary>
        /// <param name="requests">The request to execute</param>
        Task BulkWrite(IEnumerable<TelemetryParameter> requests);
    }
}
