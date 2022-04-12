using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Common;
using Quix.Snowflake.Domain.Models;

namespace Quix.Snowflake.Domain.Repositories
{
    /// <summary>
    /// Repository for persisting new <see cref="TelemetryStream"/> and reading existing
    /// </summary>
    public interface IStreamRepository
    {
        /// <summary>
        /// Bulk write capable of updating/inserting/deleting multiple things at a time
        /// </summary>
        /// <param name="insertRequests">The request to execute</param>
        Task BulkWrite(IEnumerable<WriteModel<TelemetryStream>> insertRequests);

        Task<IList<TelemetryStream>> Get(FilterDefinition<TelemetryStream> filter);
    }

    public class NullStreamRepository : IStreamRepository
    {
        public Task BulkWrite(IEnumerable<WriteModel<TelemetryStream>> insertRequests)
        {
            return Task.CompletedTask;
        }

        public Task<IList<TelemetryStream>> Get(FilterDefinition<TelemetryStream> filter)
        {
            return Task.FromResult(new List<TelemetryStream>() as IList<TelemetryStream>);
        }
    }
}
