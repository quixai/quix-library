using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Telemetry.Domain.Metadata.Models;
using Quix.Telemetry.Domain.Metadata.Repositories;

namespace Quix.Snowflake.Infrastructure.TimeSeries.Repositories
{
    public class ParameterGroupRepository : IParameterGroupRepository
    {
        public Task Save(ICollection<TelemetryParameterGroup> parameterGroups)
        {
            throw new System.NotImplementedException();
        }

        public IQueryable<TelemetryParameterGroup> GetAll()
        {
            throw new System.NotImplementedException();
        }

        public Task<long> DeleteAll(string streamId)
        {
            throw new System.NotImplementedException();
        }

        public Task BulkWrite(IEnumerable<TelemetryParameterGroup> requests)
        {
            throw new System.NotImplementedException();
        }
    }
}