using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Models;
using Quix.Snowflake.Domain.Repositories;

namespace Quix.Snowflake.Infrastructure.TimeSeries.Repositories
{
    public class ParameterRepository : IParameterRepository
    {
        public Task Save(ICollection<TelemetryParameter> parameters)
        {
            throw new System.NotImplementedException();
        }

        public IQueryable<TelemetryParameter> GetAll()
        {
            throw new System.NotImplementedException();
        }

        public Task<long> Delete(string streamId, ICollection<string> parameterIds)
        {
            throw new System.NotImplementedException();
        }

        public Task<long> DeleteAll(string streamId)
        {
            throw new System.NotImplementedException();
        }

        public Task BulkWrite(IEnumerable<TelemetryParameter> requests)
        {
            throw new System.NotImplementedException();
        }
    }
}