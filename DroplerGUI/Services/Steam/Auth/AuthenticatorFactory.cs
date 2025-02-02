using System;
using SteamKit2.Authentication;

namespace DroplerGUI.Services.Steam.Auth
{
    public static class AuthenticatorFactory
    {
        public static IAuthenticator CreateAuthenticator(int authType, string sharedSecret)
        {
            // AuthType: 0 - None, 1 - Email, 2 - TwoFactor/MobileAuth
            return authType switch
            {
                0 => new CombinedAuthenticator(),
                1 => new CombinedAuthenticator(sharedSecret, useDeviceAuth: true),
                2 => new SteamGuardAuthenticator(sharedSecret), // Используем SteamGuardAuthenticator для мобильной аутентификации
                _ => new CombinedAuthenticator() // По умолчанию пробуем без аутентификации
            };
        }
    }
} 