using System.Threading.Tasks;
using SteamKit2.Internal;

namespace DroplerGUI.Services.Steam
{
    public interface IInventory
    {
        Task<CInventory_Response> ConsumePlaytime(CInventory_ConsumePlaytime_Request request);
    }
} 