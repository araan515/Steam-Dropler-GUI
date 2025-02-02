using System;
using System.Threading.Tasks;
using SteamKit2.Authentication;

namespace DroplerGUI.Services.Steam
{
    public class SteamGuardAuthenticator : IAuthenticator
    {
        private readonly string _sharedSecret;

        public SteamGuardAuthenticator(string sharedSecret)
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
    }
}