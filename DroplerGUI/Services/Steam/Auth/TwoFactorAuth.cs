using System.Threading.Tasks;
using SteamKit2.Authentication;
using SteamKit2.Internal;

namespace DroplerGUI.Services.Steam.Auth
{
    public class TwoFactorAuth : IAuthenticator
    {
        private readonly string _sharedSecret;

        public TwoFactorAuth(string sharedSecret)
        {
            _sharedSecret = sharedSecret;
        }

        public Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            var mobileAuth = new MobileAuth { SharedSecret = _sharedSecret };
            return Task.FromResult(mobileAuth.GenerateSteamGuardCode());
        }

        public Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            return Task.FromResult<string>(null);
        }

        public Task<bool> AcceptDeviceConfirmationAsync()
        {
            return Task.FromResult(true);
        }

        public EAuthSessionGuardType NeedGuardType()
        {
            return EAuthSessionGuardType.k_EAuthSessionGuardType_DeviceCode;
        }
    }
} 