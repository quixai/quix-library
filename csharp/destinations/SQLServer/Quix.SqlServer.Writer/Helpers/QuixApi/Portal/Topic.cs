namespace Quix.SqlServer.Writer.Helpers.QuixApi.Portal
{
    /// <summary>
    /// Describes properties of a topic
    /// </summary>
    internal class Topic
    {
        /// <summary>
        /// Used in the SDK to connect to the topic
        /// </summary>
        public string Id { get; set; } 

        /// <summary>
        /// The name of the topic
        /// </summary>
        public string Name { get; set; }
        
        /// <summary>
        /// The unique identifier of the workspace the topic belongs to
        /// </summary>
        public string WorkspaceId { get; set; }
        
        /// <summary>
        /// The status of the topic
        /// </summary>
        public TopicStatus Status { get; set; }
    }
    
    /// <summary>
    /// The possible statuses of a topic
    /// </summary>
    internal enum TopicStatus
    {
        /// <summary>
        /// The topic is currently being created
        /// </summary>
        Creating,
        
        /// <summary>
        /// The topic is ready for use
        /// </summary>
        Ready,
        
        /// <summary>
        /// The topic is currently being deleted
        /// </summary>
        Deleting,
        
        /// <summary>
        /// The topic has encountered an error
        /// </summary>
        Error
    }

}