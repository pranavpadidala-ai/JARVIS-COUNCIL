using System.Windows;
using Council.App.Chat;

namespace Council.Desktop;

public partial class App : System.Windows.Application
{
    private OmniRouteLauncher? _omniRoute;

    /// <summary>
    /// JARVIS opens straight into a simple chat window. The dashboard window still exists, hidden,
    /// because it owns the tray icon, reminders, wake word and emergency stop. Start with
    /// --dashboard to open the dashboard as well. The app exits only through "Exit JARVIS" in the tray.
    /// When OmniRoute is the selected provider, JARVIS starts it and stops it again on exit.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The chat needs the saved provider/model choice before it creates its AI session.
        var selection = DesktopProviderSettingsStore.TryLoadAndApplySaved(out _);

        _omniRoute = new OmniRouteLauncher(
            OmniRouteLauncher.FindLauncher(AppContext.BaseDirectory),
            Environment.GetEnvironmentVariable("OMNIROUTE_BASE_URL") ?? "http://localhost:20128/v1");
        string? omniStatus = null;
        try { omniStatus = _omniRoute.EnsureRunning(selection?.Provider ?? Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER")); }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or UriFormatException)
        {
            omniStatus = "Could not start OmniRoute (" + ex.Message + "); JARVIS will use the local model.";
        }

        var host = new MainWindow();
        MainWindow = host;
        if (e.Args.Any(arg => string.Equals(arg, "--dashboard", StringComparison.OrdinalIgnoreCase)))
            host.Show();
        host.OpenChat();
        if (omniStatus is not null)
            host.ShowTrayMessage("JARVIS", omniStatus);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _omniRoute?.Dispose(); // frees OmniRoute's memory if JARVIS started it
        base.OnExit(e);
    }
}
