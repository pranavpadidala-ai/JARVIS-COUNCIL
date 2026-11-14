using System.Diagnostics;
using System.ComponentModel;
using Forms = System.Windows.Forms;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using WpfApplication = System.Windows.Application;
using Council.App.Chat;
using Council.Memory;
using Council.Desktop.Voice;

namespace Council.Desktop;

public partial class MainWindow : Window
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private sealed class MemoryStatus
    {
        public uint Length = 64;
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
    private readonly Forms.NotifyIcon _trayIcon;
    private bool _allowApplicationExit;
    private readonly WakeWordService _wakeWordService = new();
    private Forms.ToolStripMenuItem? _wakeWordMenuItem;
    private CancellationTokenSource? _wakeWordCancellation;
    private Task? _wakeWordLoopTask;
    private TimeSpan _lastCpu;
    private DateTime _lastCpuAt = DateTime.UtcNow;

    public MainWindow()
    {
        InitializeComponent();
        _trayIcon = CreateTrayIcon();
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        StateChanged += (_, _) =>
        {
            if (WindowState == WindowState.Minimized && !_allowApplicationExit)
            {
                WindowState = WindowState.Normal;
                Hide();
            }
        };
        WpfApplication.Current.SessionEnding += (_, _) => _allowApplicationExit = true;
        _clock.Tick += (_, _) => ClockText.Text = DateTime.Now.ToString("ddd, dd MMM yyyy  •  HH:mm:ss");
        ClockText.Text = DateTime.Now.ToString("ddd, dd MMM yyyy  •  HH:mm:ss");
        _clock.Start();

        _lastCpu = _process.TotalProcessorTime;
        _telemetry.Tick += (_, _) => UpdateTelemetry();
        UpdateTelemetry();
        _telemetry.Start();

        Closed += async (_, _) =>
        {
            _wakeWordCancellation?.Cancel();
            if (_wakeWordLoopTask is not null)
            {
                try { await _wakeWordLoopTask.WaitAsync(TimeSpan.FromSeconds(5)); }
                catch (OperationCanceledException) { }
                catch (TimeoutException) { }
            }
            _wakeWordCancellation?.Dispose();
            _clock.Stop();
            _telemetry.Stop();
            _trayIcon.Visible = false;
            _trayIcon.Dispose();
            _process.Dispose();
        };
    }


    private Forms.NotifyIcon CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open Command Center", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Open JARVIS Chat", null, (_, _) =>
        {
            RestoreFromTray();
            OpenChatWithPrompt(null);
        });
        _wakeWordMenuItem = new Forms.ToolStripMenuItem("Listen for Hey JARVIS")
        {
            CheckOnClick = true,
            ToolTipText = "Opt in to local wake-word detection. The microphone stays closed unless this item is checked."
        };
        _wakeWordMenuItem.CheckedChanged += WakeWordMenuItem_CheckedChanged;
        menu.Items.Add(_wakeWordMenuItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit JARVIS", null, (_, _) => ExitFromTray());

        var icon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.SystemIcons.Application,
            Text = "JARVIS - Right-click to open or exit",
            ContextMenuStrip = menu,
            Visible = true
        };
        icon.DoubleClick += (_, _) => RestoreFromTray();
        return icon;
    }


    private void WakeWordMenuItem_CheckedChanged(object? sender, EventArgs e)
    {
        if (_wakeWordMenuItem is null)
            return;

        if (!_wakeWordMenuItem.Checked)
        {
            StopWakeWordListener();
            return;
        }

        if (!WakeWordService.IsInstalled(out var detail))
        {
            _wakeWordMenuItem.Checked = false;
            _trayIcon.ShowBalloonTip(
                4500,
                "Wake-word pack not installed",
                detail + " Run scripts\\Install-WakeWord.ps1 from the JARVIS repository.",
                Forms.ToolTipIcon.Warning);
            return;
        }

        if (_wakeWordLoopTask is { IsCompleted: false })
            return;

        _wakeWordCancellation?.Dispose();
        _wakeWordCancellation = new CancellationTokenSource();
        _trayIcon.Text = "JARVIS - Listening for Hey JARVIS";
        _trayIcon.ShowBalloonTip(
            1800,
            "Wake-word listening enabled",
            "Local microphone detection is active. Uncheck this tray item to mute it.",
            Forms.ToolTipIcon.Info);
        _wakeWordLoopTask = RunWakeWordLoopAsync(_wakeWordCancellation.Token);
    }

    private void StopWakeWordListener()
    {
        _wakeWordCancellation?.Cancel();
        _trayIcon.Text = "JARVIS - Right-click to open or exit";
    }

    private async Task RunWakeWordLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var detected = await _wakeWordService.ListenOnceAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(detected))
                    continue;

                var voiceTurnCompleted = new TaskCompletionSource(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                await Dispatcher.InvokeAsync(() =>
                {
                    RestoreFromTray();
                    var chat = new ChatWindow
                    {
                        Owner = this,
                        StartVoiceOnLoad = true
                    };

                    EventHandler? completedHandler = null;
                    completedHandler = (_, _) =>
                    {
                        chat.VoiceTurnCompleted -= completedHandler;
                        voiceTurnCompleted.TrySetResult();
                    };
                    chat.VoiceTurnCompleted += completedHandler;
                    chat.Closed += (_, _) => voiceTurnCompleted.TrySetResult();
                    chat.Show();
                });

                // The wake-word microphone has already stopped before the chat
                // opens its separate command-capture microphone.
                await voiceTurnCompleted.Task.WaitAsync(cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // The user unchecked the tray item or exited JARVIS.
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or
                                   UnauthorizedAccessException or DllNotFoundException or
                                   BadImageFormatException or PlatformNotSupportedException)
        {
            await Dispatcher.InvokeAsync(() =>
            {
                if (_wakeWordMenuItem is not null)
                    _wakeWordMenuItem.Checked = false;
                _trayIcon.ShowBalloonTip(
                    5000,
                    "Wake-word listener stopped",
                    ex.GetBaseException().Message,
                    Forms.ToolTipIcon.Warning);
            });
        }
        finally
        {
            if (_wakeWordCancellation?.IsCancellationRequested == true)
                _trayIcon.Text = "JARVIS - Right-click to open or exit";
        }
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (_allowApplicationExit)
            return;

        e.Cancel = true;
        Hide();
        _trayIcon.ShowBalloonTip(
            1400,
            "JARVIS is still running",
            "Use the JARVIS tray icon and choose Exit JARVIS to close it.",
            Forms.ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        Show();
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;
        Activate();
    }

    private void ExitFromTray()
    {
        _allowApplicationExit = true;
        _trayIcon.Visible = false;
        WpfApplication.Current.Shutdown();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var selected = Environment.GetEnvironmentVariable("JARVIS_AI_PROVIDER")?.Trim().ToLowerInvariant();
            var providerLabel = selected switch
            {
                "ollama" => "Local Ollama",
                "gemini" => "Google Gemini",
                "openai" => "OpenAI",
                "omniroute" => "OmniRoute (allowlisted)",
                "mock" => "Mock / test mode",
                null or "" when !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("GEMINI_API_KEY")) => "Google Gemini (auto)",
                null or "" when !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("OPENAI_API_KEY")) => "OpenAI (auto)",
                null or "" => "Mock / test mode (configure a provider)",
                _ => "Unknown provider setting"
            };
            ProviderStatusText.Text = "●  " + providerLabel;

            var localData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localData))
                localData = AppContext.BaseDirectory;
            var memory = new ConversationMemoryStore(
                Path.Combine(localData, "JARVIS", "Memory", "desktop-conversation.jsonl"));
            var history = await memory.LoadRecentAsync(120);
            MemoryCountText.Text = $"{history.Count} saved messages";
        }
        catch
        {
            ProviderStatusText.Text = "●  Check provider configuration";
            MemoryCountText.Text = "Memory status unavailable";
        }
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

    private void OpenTasks_Click(object sender, RoutedEventArgs e)
    {
        var tasks = new DevelopmentTasksWindow { Owner = this };
        tasks.Show();
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

    private void TeachPreference_Click(object sender, RoutedEventArgs e)
    {
        var chat = new ChatWindow { Owner = this, InitialPrompt = "/remember " };
        chat.Show();
    }

    private void TeachWorkflow_Click(object sender, RoutedEventArgs e)
    {
        var chat = new ChatWindow { Owner = this, InitialPrompt = "/learn " };
        chat.Show();
    }

    private void ReviewMemory_Click(object sender, RoutedEventArgs e)
    {
        var chat = new ChatWindow { Owner = this, InitialPrompt = "/memories" };
        chat.Show();
    }

    private void RunWorkflow_Click(object sender, RoutedEventArgs e)
    {
        var chat = new ChatWindow { Owner = this, InitialPrompt = "Help me plan a workflow and identify which steps can be safely automated: " };
        chat.Show();
    }

    private void OpenAiCore_Click(object sender, RoutedEventArgs e)
    {
        OpenChatWithPrompt("Explain the currently configured AI provider, model, and local/cloud routing. Do not expose secrets.");
    }

    private void OpenAgents_Click(object sender, RoutedEventArgs e) => OpenChatWithPrompt("/agents");

    private void OpenConversations_Click(object sender, RoutedEventArgs e)
    {
        // Opening chat restores the recent local conversation history automatically.
        OpenChatWithPrompt(null);
    }

    private void OpenKnowledgeBase_Click(object sender, RoutedEventArgs e)
    {
        OpenChatWithPrompt("Help me search and understand files in my configured JARVIS workspace. Ask for a topic or filename if needed.");
    }

    private void OpenSkills_Click(object sender, RoutedEventArgs e) => OpenChatWithPrompt("/skills");

    private void OpenChatWithPrompt(string? prompt)
    {
        var chat = new ChatWindow { Owner = this, InitialPrompt = prompt };
        chat.Show();
    }
}