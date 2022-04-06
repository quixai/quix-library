namespace Quix.SqlServer.Domain.Models
{
    public class TopicId : StringValueObject
    {
        public TopicId(string topicId) : base (topicId?.ToLower()) // Value saved always in lowercase
        {
        }
    }
}