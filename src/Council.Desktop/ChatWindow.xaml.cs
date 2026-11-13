using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Council.App.Chat;

namespace Council.Desktop;

public partial class ChatWindow : Window
{
    public string? InitialPrompt { get; set; }\n\n    private AssistantSession? _session;
    private bool _sending;

    public ChatWindow()
    {
        InitializeComponent();
        Loaded += ChatWindow_Loaded;
    }

    private async void ChatWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _session = await AssistantSession.CreateAsync();
            ProviderStatus.Text = $"●  {_session.ProviderName}";
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

    private async void Send_Click(object sender, RoutedEventArgs e) => await SendMessageAsync();

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
        ProviderStatus.Text = "●  Thinking...";
        try
        {
            var response = await _session.SendAsync(prompt);
            AddMessage("JARVIS", response.Content, true);
            ProviderStatus.Text = $"●  {response.Provider} / {response.Model}";
        }
        catch (Exception ex)
        {
            AddMessage("JARVIS", $"I couldn't complete that request: {ex.GetBaseException().Message}", true);
            ProviderStatus.Text = "●  Provider error";
        }
        finally
        {
            _sending = false;
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