namespace Quix.Snowflake.Writer.Helpers.QuixApi.Portal
{
    /// <summary>
    /// Model describing the exception which occurred while executing a request
    /// </summary>
    internal class Exception
    {
        /// <summary>
        /// The exception message
        /// </summary>
        public string Message { get; set; }
        
        /// <summary>
        /// The correlation id used to reference this exception
        /// </summary>
        public string CorrelationId { get; set; }
    }
}