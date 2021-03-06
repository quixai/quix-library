using System.Collections.Generic;

namespace Quix.Redshift.Application.Helpers
{
    public static class Extensions
    {
        public static int DictionaryContentHash<T, K>(this Dictionary<T, K> tags)
        {
            if (tags == null) return 0;
            if (tags.Count == 0) return 0;
            unchecked
            {
                var hash = tags.Count * 397;
                foreach (var kpair in tags)
                {
                    hash ^= kpair.Value.GetHashCode();
                    hash ^= kpair.Key.GetHashCode();
                }

                return hash;
            }
        }
    }
}