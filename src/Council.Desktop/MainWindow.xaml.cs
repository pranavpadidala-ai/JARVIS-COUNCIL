using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace Council.Desktop;

public partial class MainWindow : Window
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatus
    {
        public uint Length = (uint)Marshal.SizeOf<MemoryStatus>();
        public uint MemoryLoad;
        public ulong TotalPhysical;
        public ulong AvailablePhysical;
        public ulong TotalPageFile;
        public ulong AvailablePageFile;
        public ulong TotalVirtual;
        public ulong AvailableVirtual;
        public ulong AvailableExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MemoryStatus buffer);

    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _telemetry = new() { Interval = TimeSpan.FromSeconds(2) };
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private DateTime _lastCpuAt = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("ddd, dd MMM yyyy  •  HH:mm:ss");
        ClockText.Text = DateTime.Now.ToString("ddd, dd MMM yyyy  •  HH:mm:ss");
        _clock.Start();

        _lastCpu = _process.TotalProcessorTime;
        _telemetry.Tick += (_, _) => UpdateTelemetry();
        UpdateTelemetry();
        _telemetry.Start();

        Closed += (_, _) =>
        {
            _clock.Stop();
            _telemetry.Stop();
            _process.Dispose();
        };
    }

    private void UpdateTelemetry()
    {
        try
        {
            _process.Refresh();
            var now = DateTime.UtcNow;
            var cpu = _process.TotalProcessorTime;
            var elapsed = (now - _lastCpuAt).TotalMilliseconds;
            var cpuDelta = (cpu - _lastCpu).TotalMilliseconds;
            var percent = elapsed > 0
                ? Math.Clamp(cpuDelta / (elapsed * Environment.ProcessorCount) * 100, 0, 100)
                : 0;
            CpuMetric.Text = $"{percent:0}%";
            ProcessMetric.Text = $"{_process.WorkingSet64 / 1024d / 1024d:0} MB";
            _lastCpu = cpu;
            _lastCpuAt = now;

            var memory = new MemoryStatus();
            if (GlobalMemoryStatusEx(memory) && memory.TotalPhysical > 0)
            {
                var used = 100d * (memory.TotalPhysical - memory.AvailablePhysical) / memory.TotalPhysical;
                RamMetric.Text = $"{used:0}%";
            }

            var systemRoot = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrWhiteSpace(systemRoot))
            {
                var drive = new DriveInfo(systemRoot);
                if (drive.IsReady)
                    DiskMetric.Text = $"{drive.AvailableFreeSpace / 1024d / 1024d / 1024d:0.0} GB";
            }
        }
        catch
        {
            CpuMetric.Text = "N/A";
            RamMetric.Text = "N/A";
            DiskMetric.Text = "N/A";
            ProcessMetric.Text = "N/A";
        }
    }

    private void OpenConsole_Click(object sender, RoutedEventArgs e)
    {
        var chat = new ChatWindow { Owner = this };
        chat.Show();
    }

    private void StartTask_Click(object sender, RoutedEventArgs e)
    {
        var chat = new ChatWindow { Owner = this, InitialPrompt = "I want to start a new task. Help me plan it: " };
        chat.Show();
    }

    private void RunWorkflow_Click(object sender, RoutedEventArgs e)
    {
        var chat = new ChatWindow { Owner = this, InitialPrompt = "Help me plan a workflow and identify which steps can be safely automated: " };
        chat.Show();
    }
}