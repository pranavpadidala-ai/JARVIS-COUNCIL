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
        var chat = new ChatWindow { Owner = this };
        chat.Show();
    }
}