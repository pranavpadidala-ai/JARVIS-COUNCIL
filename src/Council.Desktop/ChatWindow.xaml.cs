using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Windows.Media;
using Council.App.Chat;
using Council.App.Voice;
using System.Windows.Threading;

namespace Council.Desktop;

public partial class ChatWindow : Window
{
    public string? InitialPrompt { get; set; }

    private AssistantSession? _session;
    private bool _sending;
    private bool _speakReplies;
    private WindowsVoiceService? _voice;
    private CancellationTokenSource? _voiceCapture;
    private CancellationTokenSource? _turnCancellation;
    private DispatcherTimer? _phoneRefreshTimer;

    public ChatWindow()
    {
        InitializeComponent();
        Loaded += ChatWindow_Loaded;
        Closed += async (_, _) =>
        {
            _phoneRefreshTimer?.Stop();
            _voiceCapture?.Cancel();
            _voice?.StopAll();
            _voice?.Dispose();
            _voiceCapture?.Dispose();
            _turnCancellation?.Cancel();
            _turnCancellation?.Dispose();
            if (_session is not null)
                await _session.DisposeAsync();
        };
    }

    private async void ChatWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(InitialPrompt))
                MessageInput.Text = InitialPrompt;
            _session = await AssistantSession.CreateAsync();
            ProviderStatus.Text = $"●  {_session.ProviderName}";
            PhoneInboxButton.ToolTip = _session.PhoneBridgeStatus;
            await RefreshPhoneInboxCountAsync();
            _phoneRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _phoneRefreshTimer.Tick += async (_, _) => await RefreshPhoneInboxCountAsync();
            _phoneRefreshTimer.Start();
            foreach (var message in _session.GetRecentMessages())
                AddMessage(message.Role == "user" ? "YOU" : "JARVIS", message.Content, message.Role != "user");
            if (_session.MessageCount == 0)
                AddMessage("JARVIS", "Hello. I'm ready. What would you like me to work on?", true);
        }
        catch (Exception ex)
        {
            ProviderStatus.Text = "●  Initialization issue";
            AddMessage("JARVIS", $"I couldn't initialize the assistant: {ex.GetBaseException().Message}", true);
        }
    }

    private async void PhoneInbox_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
            return;

        var window = new PhoneInboxWindow(_session) { Owner = this };
        window.ShowDialog();
        RefreshTranscriptFromSession();
        await RefreshPhoneInboxCountAsync();
    }

    private async Task RefreshPhoneInboxCountAsync()
    {
        if (_session is null)
            return;
        try
        {
            var commands = await _session.ListPhoneCommandsAsync();
            var pending = commands.Count(command => command.Status == Council.App.Phone.PhoneCommandStatus.Queued);
            PhoneInboxButton.Content = $"PHONE INBOX ({pending})";
            PhoneInboxButton.ToolTip = _session.PhoneBridgeStatus;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            PhoneInboxButton.ToolTip = "Phone inbox unavailable: " + ex.Message;
        }
    }

    private void RefreshTranscriptFromSession()
    {
        if (_session is null)
            return;
        Transcript.Children.Clear();
        var messages = _session.GetRecentMessages();
        foreach (var message in messages)
            AddMessage(message.Role == "user" ? "YOU" : "JARVIS", message.Content, message.Role != "user");
        if (messages.Count == 0)
            AddMessage("JARVIS", "Hello. I'm ready. What would you like me to work on?", true);
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void Voice_Click(object sender, RoutedEventArgs e)
    {
        if (_voiceCapture is not null)
        {
            _voiceCapture.Cancel();
            _voice?.StopAll();
            ProviderStatus.Text = "●  Stopping microphone...";
            return;
        }

        if (!OperatingSystem.IsWindows())
        {
            ProviderStatus.Text = "●  Voice input requires Windows";
            return;
        }

        var capture = new CancellationTokenSource();
        _voiceCapture = capture;
        VoiceButton.Content = "  STOP  ";
        StopButton.IsEnabled = true;
        ProviderStatus.Text = "●  Listening... speak now";
        try
        {
            _voice ??= new WindowsVoiceService();
            var recognized = await _voice.ListenOnceAsync(capture.Token);
            if (string.IsNullOrWhiteSpace(recognized))
            {
                ProviderStatus.Text = "●  No speech recognized";
                return;
            }
            MessageInput.Text = recognized.Trim();
            await SendMessageAsync();
        }
        catch (OperationCanceledException)
        {
            ProviderStatus.Text = "●  Voice capture stopped";
        }
        catch (Exception ex)
        {
            ProviderStatus.Text = "●  Voice unavailable: " + ex.GetBaseException().Message;
        }
        finally
        {
            if (ReferenceEquals(_voiceCapture, capture)) _voiceCapture = null;
            capture.Dispose();
            VoiceButton.Content = "  VOICE  ";
            StopButton.IsEnabled = _turnCancellation is not null;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _voiceCapture?.Cancel();
        _turnCancellation?.Cancel();
        _voice?.StopAll();
        ProviderStatus.Text = "●  Cancellation requested";
    }

    private void Speak_Click(object sender, RoutedEventArgs e)
    {
        _speakReplies = !_speakReplies;
        SpeakButton.Content = _speakReplies ? "  SPEAK: ON  " : "  SPEAK: OFF  ";
        if (!_speakReplies)
        {
            _voice?.StopAll();
            ProviderStatus.Text = "●  " + (_session?.ProviderName ?? "Assistant not connected");
        }
    }


    private async void MessageInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
        {
            e.Handled = true;
            await SendMessageAsync();
        }
    }

    private async Task SendMessageAsync()
    {
        if (_sending || _session is null || string.IsNullOrWhiteSpace(MessageInput.Text))
            return;

        var prompt = MessageInput.Text.Trim();
        MessageInput.Clear();
        AddMessage("YOU", prompt, false);
        _sending = true;
        using var turnCancellation = new CancellationTokenSource();
        _turnCancellation = turnCancellation;
        StopButton.IsEnabled = true;
        ProviderStatus.Text = "●  Thinking...";
        try
        {
            var response = await _session.SendAsync(prompt, turnCancellation.Token);
            AddMessage("JARVIS", response.Content, true);
            ProviderStatus.Text = $"●  {response.Provider} / {response.Model}";
            if (_speakReplies && OperatingSystem.IsWindows())
            {
                try
                {
                    _voice ??= new WindowsVoiceService();
                    await _voice.SpeakAsync(response.Content);
                }
                catch (OperationCanceledException)
                {
                    ProviderStatus.Text = "●  " + response.Provider + " / speech stopped";
                }
                catch (Exception ex)
                {
                    ProviderStatus.Text = "●  Reply ready; speech unavailable: " + ex.GetBaseException().Message;
                }
            }
        }
        catch (OperationCanceledException) when (turnCancellation.IsCancellationRequested)
        {
            AddMessage("JARVIS", "I stopped the active request. Any action already completed before cancellation remains completed.", true);
            ProviderStatus.Text = "●  Request cancelled";
        }
        catch (Exception ex)
        {
            AddMessage("JARVIS", $"I couldn't complete that request: {ex.GetBaseException().Message}", true);
            ProviderStatus.Text = "●  Provider error";
        }
        finally
        {
            if (ReferenceEquals(_turnCancellation, turnCancellation))
                _turnCancellation = null;
            _sending = false;
            StopButton.IsEnabled = _voiceCapture is not null;
            MessageInput.Focus();
        }
    }

    private void AddMessage(string speaker, string content, bool assistant)
    {
        var card = new Border
        {
            Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(assistant ? "#10283A" : "#14202C")),
            BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(assistant ? "#1B5B70" : "#263C4D")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 10),
            HorizontalAlignment = assistant ? HorizontalAlignment.Left : HorizontalAlignment.Right,
            MaxWidth = 680
        };
        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = speaker,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(assistant ? "#39DDF4" : "#A9C4D2")),
            FontSize = 10,
            FontWeight = FontWeights.Bold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        stack.Children.Add(new TextBlock
        {
            Text = content,
            Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#D7F1FA")),
            FontSize = 13,
            TextWrapping = TextWrapping.Wrap
        });
        card.Child = stack;
        Transcript.Children.Add(card);
        TranscriptScroll.ScrollToEnd();
    }
}