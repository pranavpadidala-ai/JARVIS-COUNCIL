using System.Windows;
using Council.App.Chat;

namespace Council.Desktop;

public partial class App : System.Windows.Application
{
    /// <summary>
    /// JARVIS opens straight into a simple chat window. The dashboard window still exists, hidden,
    /// because it owns the tray icon, reminders, wake word and emergency stop. Start with
    /// --dashboard to open the dashboard as well. The app exits only through "Exit JARVIS" in the tray.
    /// </summary>
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // The chat needs the saved provider/model choice before it creates its AI session.
        DesktopProviderSettingsStore.TryLoadAndApplySaved(out _);

        var host = new MainWindow();
        MainWindow = host;
        if (e.Args.Any(arg => string.Equals(arg, "--dashboard", StringComparison.OrdinalIgnoreCase)))
            host.Show();
        host.OpenChat();
    }
}
