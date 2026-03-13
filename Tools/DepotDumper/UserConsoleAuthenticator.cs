#nullable disable
using System;
using System.Threading.Tasks;
using System.Windows;
using SteamKit2.Authentication;

namespace SolusManifestApp.Tools.DepotDumper
{
    class UserConsoleAuthenticator : IAuthenticator
    {
        private readonly TaskCompletionSource<bool> _cancellationSource = new();

        public async Task<string> GetDeviceCodeAsync(bool previousCodeWasIncorrect)
        {
            string prompt = previousCodeWasIncorrect
                ? "The previous code was incorrect. Please enter your 2FA code from your authenticator app:"
                : "Please enter your 2FA code from your authenticator app:";

            return await ShowDialogAsync(prompt);
        }

        public async Task<string> GetEmailCodeAsync(string email, bool previousCodeWasIncorrect)
        {
            string prompt = previousCodeWasIncorrect
                ? $"The previous code was incorrect. Please enter the new code sent to {email}:"
                : $"Please enter the authentication code sent to {email}:";

            return await ShowDialogAsync(prompt);
        }

        public async Task<bool> AcceptDeviceConfirmationAsync()
        {
            // For QR code authentication, this should just return true
            return await Task.FromResult(true);
        }

        private async Task<string> ShowDialogAsync(string prompt)
        {
            var tcs = new TaskCompletionSource<string>();

            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var dialog = new TwoFactorDialog(prompt)
                    {
                        Owner = Application.Current.MainWindow,
                        Topmost = true
                    };

                    var result = dialog.ShowDialog();

                    if (result == true && !dialog.WasCancelled)
                    {
                        tcs.SetResult(dialog.Code);
                    }
                    else
                    {
                        // User cancelled, throw to abort authentication
                        _cancellationSource.TrySetResult(true);
                        tcs.SetResult(string.Empty);
                    }
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return await tcs.Task;
        }
    }
}
