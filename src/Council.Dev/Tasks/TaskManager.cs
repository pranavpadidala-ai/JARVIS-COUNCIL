namespace Council.Dev.Tasks;

public sealed class TaskManager
{
    private readonly TaskStore _store;
    private readonly List<DevelopmentTask> _tasks = [];

    public TaskManager(TaskStore store)
    {
        _store = store;
    }

    public IReadOnlyList<DevelopmentTask> Tasks => _tasks;

    public async Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        var loadedTasks = await _store.LoadAsync(cancellationToken);

        _tasks.Clear();
        _tasks.AddRange(loadedTasks);
    }

    public async Task<DevelopmentTask> CreateAsync(
        string title,
        string description,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            throw new ArgumentException(
                "Task title is required.",
                nameof(title));
        }

        if (_tasks.Count >= 500)
            throw new InvalidOperationException("JARVIS can retain at most 500 development tasks.");

        var task = new DevelopmentTask
        {
            Title = title.Trim(),
            Description = description?.Trim() ?? string.Empty,
            Status = TaskStatus.Pending,
            UpdatedAtUtc = DateTime.UtcNow
        };

        _tasks.Add(task);

        await _store.SaveAsync(_tasks, cancellationToken);

        return task;
    }

    public async Task<bool> UpdateStatusAsync(
        Guid taskId,
        TaskStatus status,
        CancellationToken cancellationToken = default)
    {
        var task = Find(taskId);

        if (task is null)
        {
            return false;
        }

        task.Status = status;
        task.UpdatedAtUtc = DateTime.UtcNow;

        await _store.SaveAsync(_tasks, cancellationToken);

        return true;
    }

    public async Task<bool> UpdateStepAsync(
        Guid taskId,
        string currentStep,
        CancellationToken cancellationToken = default)
    {
        var task = Find(taskId);

        if (task is null)
        {
            return false;
        }

        task.CurrentStep = currentStep?.Trim() ?? string.Empty;
        task.UpdatedAtUtc = DateTime.UtcNow;

        await _store.SaveAsync(_tasks, cancellationToken);

        return true;
    }

    public async Task<bool> IncrementAttemptAsync(
        Guid taskId,
        CancellationToken cancellationToken = default)
    {
        var task = Find(taskId);

        if (task is null)
        {
            return false;
        }

        task.AttemptCount++;
        task.UpdatedAtUtc = DateTime.UtcNow;

        await _store.SaveAsync(_tasks, cancellationToken);

        return true;
    }

    public DevelopmentTask? Find(Guid taskId)
    {
        return _tasks.FirstOrDefault(task => task.Id == taskId);
    }

    public DevelopmentTask? GetActiveTask()
    {
        return _tasks.FirstOrDefault(task =>
            task.Status == TaskStatus.Running ||
            task.Status == TaskStatus.Paused);
    }
}
