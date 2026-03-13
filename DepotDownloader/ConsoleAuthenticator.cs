#nullable disable
// Stub class to replace console authenticator
using SteamKit2.Authentication;

namespace DepotDownloader
{
    class ConsoleAuthenticator : IAuthenticator
    {
        public System.Threading.Tasks.Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            throw new System.NotImplementedException("GUI mode authentication not yet implemented");
        }

        public System.Threading.Tasks.Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            throw new System.NotImplementedException("GUI mode authentication not yet implemented");
        }

        public System.Threading.Tasks.Task<bool> AcceptDeviceConfirmationAsync()
        {
            throw new System.NotImplementedException("GUI mode authentication not yet implemented");
        }
    }
}
