using System.Threading;
using System.Windows;
using AetherLauncher.Services;

namespace AetherLauncher;






internal static class OverlayMode
{
    
    private static Mutex? _guard;

    
    
    
    
    public static bool Start()
    {
        _guard = new Mutex(true, GameOverlay.HostMutexName, out var first);

        if (!first)
        {
            
            try
            {
                first = _guard.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                first = true;
            }
        }

        if (!first)
        {
            _guard.Dispose();
            _guard = null;
            return false;
        }

        GameOverlay.Hosted = true;

        
        
        Application.Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

        new OverlayHost().Start();

        return true;
    }
}
