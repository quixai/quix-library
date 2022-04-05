using System.Collections.Generic;
using System.Linq;

namespace Quix.Snowflake.Domain.Models
{
    /// <summary>
    /// Value object base class
    /// source: https://enterprisecraftsmanship.com/posts/value-object-better-implementation
    /// </summary>
    public abstract class ValueObjectBase
    {
        protected abstract IEnumerable<object> GetEqualityComponents();

        public override bool Equals(object obj)
        {
            if (obj == null)
                return false;

            if (this.GetType() != obj.GetType())
                return false;

            var valueObject = (ValueObjectBase)obj;

            return this.GetEqualityComponents().SequenceEqual(valueObject.GetEqualityComponents());
        }

        public override int GetHashCode()
        {
            return this.GetEqualityComponents()
                .Aggregate(1, (current, obj) =>
                {
                    unchecked
                    {
                        return current * 23 + (obj?.GetHashCode() ?? 0);
                    }
                });
        }

        public static bool operator ==(ValueObjectBase a, ValueObjectBase b)
        {
            if (ReferenceEquals(a, null) && ReferenceEquals(b, null))
                return true;

            if (ReferenceEquals(a, null) || ReferenceEquals(b, null))
                return false;

            return a.Equals(b);
        }

        public static bool operator !=(ValueObjectBase a, ValueObjectBase b)
        {
            return !(a == b);
        }
    }
}