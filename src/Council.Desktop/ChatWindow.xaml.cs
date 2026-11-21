using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfColor = System.Windows.Media.Color;
using WpfColorConverter = System.Windows.Media.ColorConverter;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using System.Windows.Media;
using Council.App.Chat;
using Council.App.Voice;
using System.Windows.Threading;

namespace Council.Desktop;

public partial class ChatWindow : Window
{
    public string? InitialPrompt { get; set; }
    public bool StartVoiceOnLoad { get; set; }
    public CancellationToken WakeWordCancellationToken { get; set; }
    public event EventHandler? VoiceTurnCompleted;

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
            var recentMessages = _session.GetRecentMessages();
            WelcomePanel.Visibility = recentMessages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            foreach (var message in recentMessages)
                AddMessage(message.Role == "user" ? "YOU" : "JARVIS", message.Content, message.Role != "user");

            if (StartVoiceOnLoad)
            {
                _speakReplies = true;
                SpeakButton.Content = "  SPEAK: ON  ";
                await CaptureVoiceCommandAsync(notifyWakeWordCompletion: true);
            }
        }
        catch (Exception ex)
        {
            ProviderStatus.Text = "●  Initialization issue";
            AddMessage("JARVIS", $"I couldn't initialize the assistant: {ex.GetBaseException().Message}", true);
            if (StartVoiceOnLoad)
                VoiceTurnCompleted?.Invoke(this, EventArgs.Empty);
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
        WelcomePanel.Visibility = messages.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var message in messages)
            AddMessage(message.Role == "user" ? "YOU" : "JARVIS", message.Content, message.Role != "user");
    }

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

    private async void Voice_Click(object sender, RoutedEventArgs e) =>
        await CaptureVoiceCommandAsync();

    private async Task CaptureVoiceCommandAsync(bool notifyWakeWordCompletion = false)
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
            if (notifyWakeWordCompletion)
                VoiceTurnCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        var capture = notifyWakeWordCompletion
            ? CancellationTokenSource.CreateLinkedTokenSource(WakeWordCancellationToken)
            : new CancellationTokenSource();
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
            if (notifyWakeWordCompletion)
                VoiceTurnCompleted?.Invoke(this, EventArgs.Empty);
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
        if (e.Key == Key.Enter && (Keyboard.Modifiers & ModifierKeys.Shift) == 0)
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

    private async void NewChat_Click(object sender, RoutedEventArgs e)
    {
        if (_session is null)
            return;
        if (_sending)
        {
            ProviderStatus.Text = "Stop the current response before starting a new chat.";
            return;
        }

        await _session.ClearAsync();
        Transcript.Children.Clear();
        WelcomePanel.Visibility = Visibility.Visible;
        MessageInput.Clear();
        ProviderStatus.Text = "ΓùÅ  " + _session.ProviderName + " ┬╖ new conversation";
        MessageInput.Focus();
    }

    private async void QuickCommand_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button button || button.Tag is not string command)
            return;
        MessageInput.Text = command;
        await SendMessageAsync();
    }

    private void Suggestion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button button && button.Tag is string prompt)
        {
            MessageInput.Text = prompt;
            MessageInput.Focus();
            MessageInput.CaretIndex = MessageInput.Text.Length;
        }
    }

    private void AddMessage(string speaker, string content, bool assistant)
    {
        WelcomePanel.Visibility = Visibility.Collapsed;

        var messageRoot = new Border
        {
            MaxWidth = 860,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Padding = new Thickness(0, 15, 0, 15),
            Background = System.Windows.Media.Brushes.Transparent
        };

        var messageStack = new StackPanel();
        if (!assistant)
        {
            var userBubble = new Border
            {
                Background = new SolidColorBrush(WpfColor.FromRgb(48, 48, 48)),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(17, 12, 17, 12),
                MaxWidth = 690,
                HorizontalAlignment = WpfHorizontalAlignment.Right
            };
            var userText = new TextBlock
            {
                Foreground = new SolidColorBrush(WpfColor.FromRgb(239, 239, 239)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22
            };
            AddInlineMarkdown(userText, content);
            userBubble.Child = userText;
            messageStack.Children.Add(userBubble);
        }
        else
        {
            var identity = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                VerticalAlignment = System.Windows.VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 10)
            };
            var avatar = new Border
            {
                Width = 27,
                Height = 27,
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(WpfColor.FromRgb(38, 62, 60)),
                Margin = new Thickness(0, 0, 9, 0)
            };
            avatar.Child = new TextBlock
            {
                Text = "Γùë",
                Foreground = new SolidColorBrush(WpfColor.FromRgb(104, 220, 207)),
                FontSize = 17,
                HorizontalAlignment = WpfHorizontalAlignment.Center,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            };
            identity.Children.Add(avatar);
            identity.Children.Add(new TextBlock
            {
                Text = speaker,
                Foreground = new SolidColorBrush(WpfColor.FromRgb(235, 235, 235)),
                FontSize = 12,
                FontWeight = System.Windows.FontWeights.SemiBold,
                VerticalAlignment = System.Windows.VerticalAlignment.Center
            });
            messageStack.Children.Add(identity);
            messageStack.Children.Add(CreateFormattedContent(content));

            var actions = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(-6, 7, 0, 0)
            };
            var copyButton = new System.Windows.Controls.Button
            {
                Content = "Copy response",
                Style = (Style)FindResource("MessageActionButton"),
                ToolTip = "Copy the complete JARVIS response"
            };
            copyButton.Click += (_, _) => CopyText(content);
            actions.Children.Add(copyButton);
            messageStack.Children.Add(actions);
        }

        messageRoot.Child = messageStack;
        Transcript.Children.Add(messageRoot);
        TranscriptScroll.Dispatcher.BeginInvoke(
            new Action(() => TranscriptScroll.ScrollToEnd()),
            System.Windows.Threading.DispatcherPriority.Background);
    }

    private StackPanel CreateFormattedContent(string content)
    {
        var panel = new StackPanel();
        var lines = content.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var codeLines = new List<string>();
        var inCode = false;
        var codeLanguage = string.Empty;

        foreach (var originalLine in lines)
        {
            var trimmed = originalLine.Trim();
            var fence = new string((char)96, 3);
            if (trimmed.StartsWith(fence, StringComparison.Ordinal))
            {
                if (!inCode)
                {
                    inCode = true;
                    codeLanguage = trimmed.Length > 3 ? trimmed.Substring(3).Trim() : string.Empty;
                    codeLines.Clear();
                }
                else
                {
                    panel.Children.Add(CreateCodeBlock(string.Join(Environment.NewLine, codeLines), codeLanguage));
                    codeLines.Clear();
                    inCode = false;
                    codeLanguage = string.Empty;
                }
                continue;
            }

            if (inCode)
            {
                codeLines.Add(originalLine);
                continue;
            }

            if (string.IsNullOrWhiteSpace(originalLine))
                continue;

            var heading = System.Text.RegularExpressions.Regex.Match(originalLine, @"^\s{0,3}(#{1,3})\s+(.+)$");
            if (heading.Success)
            {
                var level = heading.Groups[1].Length;
                var headingText = new TextBlock
                {
                    FontSize = level == 1 ? 22 : level == 2 ? 18 : 16,
                    FontWeight = System.Windows.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(242, 242, 242)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 13, 0, 7)
                };
                AddInlineMarkdown(headingText, heading.Groups[2].Value);
                panel.Children.Add(headingText);
                continue;
            }

            if (System.Text.RegularExpressions.Regex.IsMatch(trimmed, @"^([-*_])\1{2,}$"))
            {
                panel.Children.Add(new Border
                {
                    Height = 1,
                    Background = new SolidColorBrush(WpfColor.FromRgb(65, 65, 65)),
                    Margin = new Thickness(0, 10, 0, 13)
                });
                continue;
            }

            var listItem = System.Text.RegularExpressions.Regex.Match(originalLine, @"^\s*(?:([-*+])|(\d+)\.)\s+(.+)$");
            if (listItem.Success)
            {
                var row = new Grid { Margin = new Thickness(0, 2, 0, 5) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(27) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var marker = new TextBlock
                {
                    Text = listItem.Groups[2].Success ? listItem.Groups[2].Value + "." : "ΓÇó",
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(190, 190, 190)),
                    FontSize = 14
                };
                var itemText = new TextBlock
                {
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(225, 225, 225)),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 22
                };
                AddInlineMarkdown(itemText, listItem.Groups[3].Value);
                Grid.SetColumn(marker, 0);
                Grid.SetColumn(itemText, 1);
                row.Children.Add(marker);
                row.Children.Add(itemText);
                panel.Children.Add(row);
                continue;
            }

            var paragraph = new TextBlock
            {
                Foreground = new SolidColorBrush(WpfColor.FromRgb(225, 225, 225)),
                FontSize = 14,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22,
                Margin = new Thickness(0, 0, 0, 8)
            };
            AddInlineMarkdown(paragraph, originalLine);
            panel.Children.Add(paragraph);
        }

        if (inCode && codeLines.Count > 0)
            panel.Children.Add(CreateCodeBlock(string.Join(Environment.NewLine, codeLines), codeLanguage));

        return panel;
    }

    private static void AddInlineMarkdown(TextBlock target, string text)
    {
        var pattern = @"(\*\*.+?\*\*|\x60.+?\x60|\*.+?\*)";
        var matches = System.Text.RegularExpressions.Regex.Matches(text, pattern);
        var cursor = 0;

        foreach (System.Text.RegularExpressions.Match match in matches)
        {
            if (match.Index > cursor)
                target.Inlines.Add(new System.Windows.Documents.Run(text.Substring(cursor, match.Index - cursor)));

            var token = match.Value;
            if (token.StartsWith("**", StringComparison.Ordinal) && token.EndsWith("**", StringComparison.Ordinal))
            {
                target.Inlines.Add(new System.Windows.Documents.Run(token.Substring(2, token.Length - 4))
                {
                    FontWeight = System.Windows.FontWeights.SemiBold
                });
            }
            else if (token.Length >= 2 && token[0] == (char)96 && token[token.Length - 1] == (char)96)
            {
                target.Inlines.Add(new System.Windows.Documents.Run(token.Substring(1, token.Length - 2))
                {
                    FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                    FontSize = 13,
                    Foreground = new SolidColorBrush(WpfColor.FromRgb(171, 215, 205)),
                    Background = new SolidColorBrush(WpfColor.FromRgb(48, 55, 53))
                });
            }
            else if (token.StartsWith("*", StringComparison.Ordinal) && token.EndsWith("*", StringComparison.Ordinal))
            {
                target.Inlines.Add(new System.Windows.Documents.Run(token.Substring(1, token.Length - 2))
                {
                    FontStyle = System.Windows.FontStyles.Italic
                });
            }
            cursor = match.Index + match.Length;
        }

        if (cursor < text.Length)
            target.Inlines.Add(new System.Windows.Documents.Run(text.Substring(cursor)));
        if (matches.Count == 0)
            target.Text = text;
    }

    private Border CreateCodeBlock(string code, string language)
    {
        var container = new Border
        {
            Background = new SolidColorBrush(WpfColor.FromRgb(25, 25, 25)),
            BorderBrush = new SolidColorBrush(WpfColor.FromRgb(61, 61, 61)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 8, 0, 13)
        };

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        var label = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(language) ? "CODE" : language.ToUpperInvariant(),
            Foreground = new SolidColorBrush(WpfColor.FromRgb(158, 158, 158)),
            FontSize = 10,
            FontWeight = System.Windows.FontWeights.SemiBold,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        DockPanel.SetDock(label, Dock.Left);
        header.Children.Add(label);

        var copyCode = new System.Windows.Controls.Button
        {
            Content = "Copy code",
            Style = (Style)FindResource("MessageActionButton"),
            HorizontalAlignment = WpfHorizontalAlignment.Right
        };
        copyCode.Click += (_, _) => CopyText(code);
        DockPanel.SetDock(copyCode, Dock.Right);
        header.Children.Add(copyCode);

        var codeText = new System.Windows.Controls.TextBox
        {
            Text = code,
            IsReadOnly = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Background = System.Windows.Media.Brushes.Transparent,
            Foreground = new SolidColorBrush(WpfColor.FromRgb(220, 220, 220)),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            FontSize = 12,
            MinHeight = 34,
            MaxHeight = 250,
            SelectionBrush = new SolidColorBrush(WpfColor.FromRgb(49, 105, 95)),
            SelectionTextBrush = System.Windows.Media.Brushes.White
        };

        Grid.SetRow(header, 0);
        Grid.SetRow(codeText, 1);
        layout.Children.Add(header);
        layout.Children.Add(codeText);
        container.Child = layout;
        return container;
    }

    private void CopyText(string text)
    {
        try
        {
            if (!string.IsNullOrEmpty(text))
                System.Windows.Clipboard.SetText(text);
        }
        catch (System.Runtime.InteropServices.ExternalException ex)
        {
            ProviderStatus.Text = "Clipboard temporarily unavailable: " + ex.Message;
        }
    }
}