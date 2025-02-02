using System.Threading.Tasks;
using SteamKit2.Authentication;

namespace DroplerGUI.Services.Steam.Auth
{
    public abstract class BaseAuthenticator : IAuthenticator
    {
        protected readonly string _sharedSecret;

        protected BaseAuthenticator(string sharedSecret = null)
        {
            _sharedSecret = sharedSecret;
        }

        public abstract Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect);
        public abstract Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect);
        
        public virtual Task<bool> AcceptDeviceConfirmationAsync()
        {
            return Task.FromResult(true);
        }

        protected string GenerateSteamGuardCode()
        {
            if (string.IsNullOrEmpty(_sharedSecret))
                return null;
                
            var mobileAuth = new MobileAuth { SharedSecret = _sharedSecret };
            return mobileAuth.GenerateSteamGuardCode();
        }
    }
} 