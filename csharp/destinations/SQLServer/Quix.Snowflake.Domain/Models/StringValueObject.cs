using System.Collections.Generic;

namespace Quix.Snowflake.Domain.Models
{
    /// <summary>
    /// Simple string value object with a single value representation.
    /// </summary>
    public class StringValueObject: ValueObjectBase
    {
        public string Value { get; }

        public bool CaseSensitive { get; }

        public StringValueObject(string value, bool caseSensitive = true)
        {
            this.Value = value;
            this.CaseSensitive = caseSensitive;
        }

        /// <summary>
        /// List of components to compare in Equality comparisons.
        /// </summary>
        /// <returns>List of </returns>
        protected override IEnumerable<object> GetEqualityComponents()
        {
            if (this.CaseSensitive)
            {
                yield return this.Value;
            }
            else
            {
                yield return this.Value.ToLower();
            }
        }

        public override string ToString() => this.Value;
    }
}