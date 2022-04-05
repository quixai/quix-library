namespace Quix.Snowflake.Application.Models
{
    public class DiscardRange
    {
        /// <summary>
        /// Exclusive before, if value is 5, then values less than 5 should be discarded
        /// </summary>
        public long DiscardBefore { get; set; }
        
        
        /// <summary>
        /// Exclusive after, if value is 5, then values greater than 5 should be discarded
        /// </summary>
        public long DiscardAfter { get; set; }

        public static DiscardRange NoDiscard = new DiscardRange()
        {
            DiscardAfter = long.MaxValue,
            DiscardBefore = long.MinValue
        };
        
        public static DiscardRange AllDiscard = new DiscardRange()
        {
            DiscardAfter = 0,
            DiscardBefore = 1
        };
    }
}