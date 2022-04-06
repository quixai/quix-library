using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.SqlServer.Domain.Models;
using Quix.SqlServer.Domain.Repositories;

namespace Quix.SqlServer.Infrastructure.TimeSeries.Repositories
{
    public class EventRepository : IEventRepository
    {
        public Task Save(ICollection<TelemetryEvent> events)
        {
            throw new System.NotImplementedException();
        }

        public IQueryable<TelemetryEvent> GetAll()
        {
            throw new System.NotImplementedException();
        }

        public Task<long> Delete(string streamId, ICollection<string> eventIds)
        {
            throw new System.NotImplementedException();
        }

        public Task<long> DeleteAll(string streamId)
        {
            throw new System.NotImplementedException();
        }

        public Task BulkWrite(IEnumerable<TelemetryEvent> requests)
        {
            throw new System.NotImplementedException();
        }
    }
}