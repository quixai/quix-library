using System.Threading.Tasks;

namespace Quix.SqlServer.Domain.Common
{
    public interface IRequiresSetup
    {
        Task Setup();
    }
}