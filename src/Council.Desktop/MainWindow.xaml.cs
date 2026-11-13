using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace Council.Desktop;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };

    public MainWindow()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("ddd, dd MMM yyyy  •  HH:mm:ss");
        ClockText.Text = DateTime.Now.ToString("ddd, dd MMM yyyy  •  HH:mm:ss");
        _clock.Start();
        Closed += (_, _) => _clock.Stop();
    }

    private void OpenConsole_Click(object sender, RoutedEventArgs e)
    {
        var root = AppContext.BaseDirectory;
        var candidates = new[]
        {
            Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "Council.App", "bin", "Debug", "net8.0", "Council.App.exe")),
            Path.GetFullPath(Path.Combine(root, "..", "..", "..", "..", "Council.App", "bin", "Release", "net8.0", "Council.App.exe"))
        };
        var executable = candidates.FirstOrDefault(File.Exists);
        if (executable is null)
        {
            MessageBox.Show("The chat runtime is not installed in this build yet. The Command Center shell is ready; runtime integration is the next step.", "JARVIS", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = true });
    }
}