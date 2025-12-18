using System.Threading.Tasks;

namespace CenterHubNew.MVVM.Services
{
    public interface ISystemMonitorService
    {
        Task<SystemInfo> GetSystemInfoAsync();
    }
}
