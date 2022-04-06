﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Quix.Snowflake.Domain.Models;
using Quix.Snowflake.Domain.Repositories;

namespace Quix.Snowflake.Infrastructure.TimeSeries.Repositories
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