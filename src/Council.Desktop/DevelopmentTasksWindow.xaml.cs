using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Council.App;
using Council.Dev.Tasks;
using DevTaskStatus = Council.Dev.Tasks.TaskStatus;

namespace Council.Desktop;

public partial class DevelopmentTasksWindow : Window
{
    private readonly DevelopmentTaskQueueService _queue = new();
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(5) };
    private bool _busy;

    public DevelopmentTasksWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await RefreshAsync();
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(
            (TasksList.SelectedItem as DevelopmentTask)?.Id);
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
    }

    private async Task RefreshAsync(Guid? preserveSelection = null)
    {
        if (_busy)
            return;

        try
        {
            var tasks = await _queue.ListAsync();
            TasksList.ItemsSource = tasks;
            var selected = preserveSelection.HasValue
                ? tasks.FirstOrDefault(task => task.Id == preserveSelection.Value)
                : tasks.FirstOrDefault(task => task.Status is DevTaskStatus.Pending or DevTaskStatus.Running or DevTaskStatus.Paused or DevTaskStatus.Failed);
            if (selected is not null)
                TasksList.SelectedItem = selected;

            TaskCount.Text = $"{tasks.Count} task(s)";
            if (tasks.Count == 0)
                StatusText.Text = "No development tasks yet. Create one below, or ask JARVIS in chat to plan a task.";
            else
            {
                var waiting = tasks.Count(task => task.IsApproved && task.ApprovalConsumedAtUtc is null);
                var running = tasks.Count(task => task.Status == DevTaskStatus.Running);
                StatusText.Text = $"Saved: {tasks.Count} · Approved and waiting: {waiting} · Running: {running}. The background worker is optional and must be started separately.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            StatusText.Text = "Could not read the development task queue: " + ex.GetBaseException().Message;
        }
    }

    private void TasksList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (TasksList.SelectedItem is not DevelopmentTask task)
        {
            SelectedTitle.Text = "Select a task or create a new one.";
            SelectedStatus.Text = "—";
            SelectedDescription.Text = "—";
            SelectedStep.Text = "—";
            UpdateButtons(null);
            return;
        }

        SelectedTitle.Text = task.Title;
        var approval = task.IsApproved && task.ApprovalConsumedAtUtc is null
            ? "APPROVED · waiting for background worker"
            : task.ApprovalConsumedAtUtc.HasValue
                ? "APPROVAL CONSUMED · run claimed"
                : "NOT APPROVED";
        SelectedStatus.Text = $"{task.Status} · {approval} · Attempts: {task.AttemptCount} · Updated: {task.UpdatedAtUtc.ToLocalTime():g}";
        SelectedDescription.Text = string.IsNullOrWhiteSpace(task.Description) ? "(No description supplied.)" : task.Description;
        SelectedStep.Text = string.IsNullOrWhiteSpace(task.CurrentStep) ? "No step recorded yet." : task.CurrentStep;
        UpdateButtons(task);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await RefreshAsync((TasksList.SelectedItem as DevelopmentTask)?.Id);

    private async void Create_Click(object sender, RoutedEventArgs e)
    {
        var title = NewTaskTitle.Text.Trim();
        var description = NewTaskDescription.Text.Trim();
        if (title.Length == 0)
        {
            StatusText.Text = "Enter a short task title first.";
            NewTaskTitle.Focus();
            return;
        }
        if (title.Length > 200 || description.Length > 8000)
        {
            StatusText.Text = "Task title is limited to 200 characters and description to 8,000 characters.";
            return;
        }

        SetBusy(true);
        try
        {
            var created = await _queue.CreateAsync(title, description);
            NewTaskTitle.Clear();
            NewTaskDescription.Clear();
            StatusText.Text = $"Created '{created.Title}'. It is Pending and has NOT been approved or run.";
            await RefreshAsync(created.Id);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not create the task: " + ex.GetBaseException().Message;
        }
        finally
        {
            SetBusy(false);
            UpdateButtons(TasksList.SelectedItem as DevelopmentTask);
        }
    }

    private async void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (TasksList.SelectedItem is not DevelopmentTask task)
            return;

        var answer = MessageBox.Show(
            $"Approve '{task.Title}' for one background execution?\n\nThe local development agent must be running. It will only run from a clean Git tree, create a checkpoint, validate with build/tests, and commit after successful validation.",
            "Approve background development task",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (answer != MessageBoxResult.Yes)
            return;

        SetBusy(true);
        try
        {
            var approved = await _queue.ApproveAsync(task.Id);
            StatusText.Text = approved
                ? "Approved for one background run. Start the optional JARVIS development agent if it is not already running."
                : "Approval was not applied. The task may have been claimed, completed or removed.";
            await RefreshAsync(task.Id);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not approve the task: " + ex.GetBaseException().Message;
        }
        finally
        {
            SetBusy(false);
            UpdateButtons(TasksList.SelectedItem as DevelopmentTask);
        }
    }

    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (TasksList.SelectedItem is not DevelopmentTask task)
            return;

        SetBusy(true);
        try
        {
            var revoked = await _queue.RevokeApprovalAsync(task.Id);
            StatusText.Text = revoked
                ? "Background approval revoked. The task will not be claimed by the worker."
                : "Approval could not be revoked; the worker may already have claimed the task.";
            await RefreshAsync(task.Id);
        }
        catch (Exception ex)
        {
            StatusText.Text = "Could not revoke approval: " + ex.GetBaseException().Message;
        }
        finally
        {
            SetBusy(false);
            UpdateButtons(TasksList.SelectedItem as DevelopmentTask);
        }
    }

    private void RunNow_Click(object sender, RoutedEventArgs e)
    {
        if (TasksList.SelectedItem is not DevelopmentTask task)
            return;

        var answer = MessageBox.Show(
            $"Open a JARVIS chat with an explicit run request for '{task.Title}'?\n\nYou will review the request and press SEND in chat to start the guarded run.",
            "Prepare foreground task run",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes)
            return;

        var chat = new ChatWindow
        {
            Owner = this,
            InitialPrompt = $"Run the persistent development task {task.Id}. I explicitly authorize running this task now. Follow the guarded workflow and report the plan, changes, build and test results."
        };
        chat.Show();
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        CreateButton.IsEnabled = !busy;
        UpdateButtons(TasksList.SelectedItem as DevelopmentTask);
    }

    private void UpdateButtons(DevelopmentTask? task)
    {
        var eligible = !_busy && task is not null &&
            task.Status is not (DevTaskStatus.Completed or DevTaskStatus.Running) &&
            !task.ApprovalConsumedAtUtc.HasValue;
        ApproveButton.IsEnabled = eligible && task is not null && !task.IsApproved;
        RevokeButton.IsEnabled = eligible && task is not null && task.IsApproved;
        RunButton.IsEnabled = eligible;
    }
}
