using System.Threading.Tasks;

namespace DroplerGUI.Services.Steam.Auth
{
    public class CombinedAuthenticator : BaseAuthenticator
    {
        private readonly bool _useDeviceAuth;
        private readonly bool _useTwoFactor;

        public CombinedAuthenticator(string sharedSecret = null, bool useDeviceAuth = false, bool useTwoFactor = false) 
            : base(sharedSecret)
        {
            _useDeviceAuth = useDeviceAuth;
            _useTwoFactor = useTwoFactor;
        }

        public override Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            if (_useDeviceAuth)
            {
                // Здесь можно добавить дополнительную логику для device auth
                return Task.FromResult(GenerateSteamGuardCode());
            }
            return Task.FromResult<string>(null);
        }

        public override Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            if (_useTwoFactor)
            {
                // Здесь можно добавить дополнительную логику для two-factor auth
                return Task.FromResult(GenerateSteamGuardCode());
            }
            return Task.FromResult<string>(null);
        }

        public override Task<bool> AcceptDeviceConfirmationAsync()
        {
            // Можно добавить дополнительную логику подтверждения устройства
            return Task.FromResult(_useDeviceAuth);
        }
    }
} 