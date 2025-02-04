using System;
using SteamKit2.Authentication;

namespace DroplerGUI.Services.Steam.Auth
{
    public static class AuthenticatorFactory
    {
        public static IAuthenticator CreateAuthenticator(string sharedSecret)
        {
            return new SteamGuardAuthenticator(sharedSecret);
        }
    }
} 