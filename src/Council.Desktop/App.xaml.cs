using System.Windows;

namespace Council.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var startHidden = e.Args.Any(argument =>
            string.Equals(argument, "--start-hidden", StringComparison.OrdinalIgnoreCase));

        var window = new MainWindow(startHidden);
        MainWindow = window;
        window.Show();
    }
}
