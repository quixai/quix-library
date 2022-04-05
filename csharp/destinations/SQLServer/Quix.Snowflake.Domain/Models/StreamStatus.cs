namespace Quix.Snowflake.Domain.Models
{
    public enum StreamStatus
    {
        /// <summary>
        /// Default Status until proven otherwise
        /// </summary>
        Open,

        /// <summary>
        /// When stream finished normally
        /// </summary>
        Closed,

        /// <summary>
        /// When stream finished with aborted status
        /// </summary>
        Aborted,

        /// <summary>
        /// When stream finished with terminated status
        /// </summary>
        Terminated,

        /// <summary>
        /// When stream got temporarily interrupted, but expected to transition to another status soon
        /// </summary>
        Interrupted,

        /// <summary>
        /// The stream is marked for deletion
        /// </summary>
        Deleting,
        
        /// <summary>
        /// The stream is marked as soft deleted
        /// </summary>
        SoftDeleted,
        
        /// <summary>
        /// When stream is open, but no data arrived for it in last W0 minutes
        /// </summary>
        Idle,
    };
}