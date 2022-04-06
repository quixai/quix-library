using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Quix.SqlServer.Infrastructure.Shared.Extensions
{
    public static class QueryableExtensions
    {
        public static Task<List<T>> ToListAsync<T>(this IQueryable<T> queryable)
        {
            //todo mongo stuff
            // if (queryable is IMongoQueryable<T> mongoQueryable)
            // {
            //     return IAsyncCursorSourceExtensions.ToListAsync(mongoQueryable);
            // }

            var list = queryable.ToList();
            return Task.FromResult(list);
        }
        
        public static Task<int> CountAsync<T>(this IQueryable<T> queryable)
        {
            //todo mongo stuff

            // if (queryable is IMongoQueryable<T> mongoQueryable)
            // {
            //     return MongoQueryable.CountAsync(mongoQueryable);
            // }

            var count = queryable.Count();
            return Task.FromResult(count);
        }
        
        public static Task<T> FirstOrDefaultAsync<T>(this IQueryable<T> queryable)
        {
            //todo mongo stuff
            //
            // if (queryable is IMongoQueryable<T> mongoQueryable)
            // {
            //     return MongoQueryable.FirstOrDefaultAsync(mongoQueryable);
            //}

            var result = queryable.FirstOrDefault();
            return Task.FromResult(result);
        }
    }
}