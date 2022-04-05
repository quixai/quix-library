using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Models;
using Quix.Snowflake.Domain.Repositories;
using Quix.Telemetry.Domain.Metadata.Models;

namespace Quix.Snowflake.Infrastructure.TimeSeries.Repositories
{
    
    //TODO need to implement this.
    
    public class StreamRepository : IStreamRepository
    {
        public StreamRepository()
        {
            
        }
        
        public Task Save(TelemetryStream stream)
        {
            throw new System.NotImplementedException();
        }

        public Task Update(string streamId, TelemetryStream update)
        {
            throw new System.NotImplementedException();
        }

        public Task BulkWrite(IEnumerable<TelemetryStream> insertRequests)
        {
            throw new System.NotImplementedException();
        }

        public Task Delete(string streamId)
        {
            throw new System.NotImplementedException();
        }

        public Task<long> MarkDeleted(ICollection<string> streamIds, bool hardDelete)
        {
            throw new System.NotImplementedException();
        }

        public Task<long> Restore(ICollection<string> streamIds)
        {
            throw new System.NotImplementedException();
        }

        public IQueryable<TelemetryStream> GetAll()
        {
            throw new System.NotImplementedException();
        }
    }
}