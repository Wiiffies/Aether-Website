using Microsoft.Win32;

namespace AetherLauncher.Services;







public static class ProtocolHandler
{
    private const string KeyPath = @"Software\Classes\roblox-player";
    private const string CommandPath = KeyPath + @"\shell\open\command";

    
    public static string? ReadCurrentCommand()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(CommandPath);
            return key?.GetValue(null) as string;
        }
        catch
        {
            return null;
        }
    }

    public static string BuildCommand(string exePath) => $"\"{exePath}\" --launch \"%1\"";

    public static bool IsRegisteredFor(string exePath)
    {
        var current = ReadCurrentCommand();
        return current is not null && current.Contains(exePath, StringComparison.OrdinalIgnoreCase);
    }

    
    public static bool Register(string exePath, out string message)
    {
        message = "";
        try
        {
            using (var protocolKey = Registry.CurrentUser.CreateSubKey(KeyPath))
            {
                protocolKey.SetValue("URL Protocol", "");
            }

            using (var commandKey = Registry.CurrentUser.CreateSubKey(CommandPath))
            {
                commandKey.SetValue(null, BuildCommand(exePath));
            }

            message = "Aether is now the roblox-player handler.";
            Log.Info(message);
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not register the protocol handler: {ex.Message}";
            Log.Error(message);
            return false;
        }
    }

    
    public static bool Restore(string? savedCommand, out string message)
    {
        message = "";
        try
        {
            if (string.IsNullOrWhiteSpace(savedCommand))
            {
                
                Registry.CurrentUser.DeleteSubKeyTree(KeyPath, throwOnMissingSubKey: false);
                message = "Protocol handler entry removed.";
            }
            else
            {
                using var commandKey = Registry.CurrentUser.CreateSubKey(CommandPath);
                commandKey.SetValue(null, savedCommand);
                message = $"Previous handler restored: {savedCommand}";
            }

            Log.Info(message);
            return true;
        }
        catch (Exception ex)
        {
            message = $"Could not restore the handler: {ex.Message}";
            Log.Error(message);
            return false;
        }
    }
}
