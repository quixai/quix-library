namespace Quix.Snowflake.Domain.Models
{
    /// <summary>
    /// Name of the Topic. Unique within same Workspace.
    /// It is used internally in Kubernetes to name some services related with the topic, like Telemetry Writer pods and services.
    /// Internally usage: part of Kafka topic name, part of Kubernetes pod/service name.
    /// </summary>
    public class TopicName : StringValueObject
    {
        private const int MaxLenghtLimit = 43; // Limited because limitation of Resource Names within Kubernetes Namespace. 20 characters for ServiceName, and 43 for Topic Name.

        public TopicName(string topicName) : base (topicName?.ToLower()) // Value saved always in lowercase
        {
        }
    }
}