using System.Threading.Tasks;
using SteamKit2.Authentication;

namespace DroplerGUI.Services.Steam.Auth
{
    public class DeviceAuth : IAuthenticator
    {
        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            return Task.FromResult<string>(null);
        }

        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            return Task.FromResult<string>(null);
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            return Task.FromResult(true);
        }
    }
} 