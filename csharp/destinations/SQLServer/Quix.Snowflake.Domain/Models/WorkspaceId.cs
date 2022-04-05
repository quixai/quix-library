namespace Quix.Snowflake.Domain.Models
{
    /// <summary>
    /// Workspace identifier.
    /// It is used internally and externally, for example to reference a workspace in a url.
    /// Internally usage: Kubernetes namespace, internal folder names, telemetry database name.
    /// Externally usage: Prefix of url for public services.
    /// </summary>
    public class WorkspaceId : StringValueObject
    {

        public WorkspaceId(string workspaceId) : base (workspaceId?.ToLower()) // Value saved always in lowercase
        {
        }
    }
}
