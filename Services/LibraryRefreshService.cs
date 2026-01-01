using System;

namespace SolusManifestApp.Services
{
    public class LibraryRefreshService
    {
        public event EventHandler<GameInstalledEventArgs>? GameInstalled;
        public event EventHandler<string>? GameUninstalled;

        public void NotifyGameInstalled(string appId, bool isGreenLuma = false)
        {
            GameInstalled?.Invoke(this, new GameInstalledEventArgs(appId));
        }

        public void NotifyGameUninstalled(string appId)
        {
            GameUninstalled?.Invoke(this, appId);
        }
    }

    public class GameInstalledEventArgs : EventArgs
    {
        public string AppId { get; }

        public GameInstalledEventArgs(string appId)
        {
            AppId = appId;
        }
    }
}
