using System.Threading.Tasks;

namespace DroplerGUI.Services.Steam.Auth
{
    public class SteamGuardAuthenticator : BaseAuthenticator
    {
        public SteamGuardAuthenticator(string sharedSecret) : base(sharedSecret)
        {
        }

        public override Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            return Task.FromResult(GenerateSteamGuardCode());
        }

        public override Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            return Task.FromResult<string>(null);
        }
    }
} 