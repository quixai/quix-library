using System.Threading.Tasks;

namespace Quix.Snowflake.Domain.Common
{
    public interface IRequiresSetup
    {
        Task Setup();
    }
}