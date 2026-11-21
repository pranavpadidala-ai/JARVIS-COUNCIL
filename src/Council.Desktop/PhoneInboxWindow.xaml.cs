using System.IO;
using System.Windows;
using WpfMessageBox = System.Windows.MessageBox;
using System.Windows.Controls;
using Council.App.Chat;
using Council.App.Phone;

namespace Council.Desktop;

public partial class PhoneInboxWindow : Window
{
    private readonly AssistantSession _session;
    private bool _busy;

    public PhoneInboxWindow(AssistantSession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        InitializeComponent();
        BridgeStatus.Text = _session.PhoneBridgeStatus;
        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task RefreshAsync(Guid? preserveSelection = null)
    {
        if (_busy)
            return;

        try
        {
            var items = await _session.ListPhoneCommandsAsync();
            var ordered = items.OrderByDescending(item => item.CreatedAtUtc).ToArray();
            CommandsList.ItemsSource = ordered;

            var selected = preserveSelection.HasValue
                ? ordered.FirstOrDefault(item => item.Id == preserveSelection.Value)
                : ordered.FirstOrDefault(item => item.Status == PhoneCommandStatus.Queued);
            if (selected is not null)
                CommandsList.SelectedItem = selected;

            var queued = items.Count(item => item.Status == PhoneCommandStatus.Queued);
            var processing = items.Count(item => item.Status == PhoneCommandStatus.Processing);
            StatusText.Text = $"Waiting for approval: {queued} · Processing: {processing} · Recent history: {items.Count}";
            if (items.Count == 0)
                StatusText.Text += " · No phone requests received yet.";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            StatusText.Text = "Phone inbox could not be read: " + ex.Message;
        }
    }

    private void CommandsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandsList.SelectedItem is not PhoneCommand command)
        {
            CommandText.Text = "Select a phone request to review it.";
            CommandStatus.Text = "—";
            CommandResult.Text = "No response yet.";
            ApproveButton.IsEnabled = false;
            RejectButton.IsEnabled = false;
            return;
        }

        CommandText.Text = command.Text;
        CommandStatus.Text = command.Status.ToString();
        CommandResult.Text = string.IsNullOrWhiteSpace(command.Result) ? "No response yet." : command.Result;
        ApproveButton.IsEnabled = !_busy && command.Status == PhoneCommandStatus.Queued;
        RejectButton.IsEnabled = !_busy && command.Status == PhoneCommandStatus.Queued;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync((CommandsList.SelectedItem as PhoneCommand)?.Id);

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (CommandsList.SelectedItem is not PhoneCommand command ||
            command.Status != PhoneCommandStatus.Queued)
            return;

        _busy = true;
        UpdateActionButtons();
        try
        {
            var rejected = await _session.RejectPhoneCommandAsync(command.Id);
            StatusText.Text = rejected
                ? "Request rejected. It will not be executed."
                : "Request was already claimed or removed by another JARVIS session.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not reject the request: " + ex.GetBaseException().Message;
        }
        finally
        {
            _busy = false;
            await RefreshAsync(command.Id);
        }
    }

    private async void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (CommandsList.SelectedItem is not PhoneCommand command ||
            command.Status != PhoneCommandStatus.Queued)
            return;

        var confirmation = WpfMessageBox.Show(
            "Run this phone request on the laptop?\n\n" + command.Text,
            "Approve phone request",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirmation != MessageBoxResult.Yes)
            return;

        _busy = true;
        UpdateActionButtons();
        StatusText.Text = "JARVIS is processing the approved phone request. This window can remain open.";
        try
        {
            var response = await _session.ApprovePhoneCommandAsync(command.Id);
            CommandResult.Text = response.Content;
            StatusText.Text = "JARVIS finished processing the request. The phone will receive this response.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Phone request was cancelled. A fresh request can be submitted from the phone.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "JARVIS could not complete the phone request: " + ex.GetBaseException().Message;
        }
        finally
        {
            _busy = false;
            await RefreshAsync(command.Id);
        }
    }

    private void UpdateActionButtons()
    {
        var enabled = !_busy &&
            CommandsList.SelectedItem is PhoneCommand command &&
            command.Status == PhoneCommandStatus.Queued;
        ApproveButton.IsEnabled = enabled;
        RejectButton.IsEnabled = enabled;
    }
}
