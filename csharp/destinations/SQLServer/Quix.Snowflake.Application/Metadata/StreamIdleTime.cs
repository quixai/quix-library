namespace Quix.Snowflake.Application.Metadata
{
    public class StreamIdleTime
    {
        public StreamIdleTime(int value)
        {
            Value = value;
        }

        /// <summary>
        /// The value in ms
        /// </summary>
        public readonly int Value;
    }
}