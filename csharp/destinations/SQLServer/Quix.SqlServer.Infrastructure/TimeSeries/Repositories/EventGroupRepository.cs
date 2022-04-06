using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.SqlServer.Domain.Models;
using Quix.SqlServer.Domain.Repositories;

namespace Quix.SqlServer.Infrastructure.TimeSeries.Repositories
{
    public class EventGroupRepository : IEventGroupRepository
    {
        public Task Save(ICollection<TelemetryEventGroup> eventGroups)
        {
            throw new System.NotImplementedException();
        }

        public IQueryable<TelemetryEventGroup> GetAll()
        {
            throw new System.NotImplementedException();
        }

        public Task<long> Delete(string streamId, ICollection<string> groupPaths)
        {
            throw new System.NotImplementedException();
        }

        public Task<long> DeleteAll(string streamId)
        {
            throw new System.NotImplementedException();
        }

        public Task BulkWrite(IEnumerable<TelemetryEventGroup> requests)
        {
            throw new System.NotImplementedException();
        }
    }
}