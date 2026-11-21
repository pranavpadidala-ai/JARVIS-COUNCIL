using Council.Dev.Actions;
using Council.Dev.Planning;
using Council.Dev.Tasks;
using Council.Dev.Terminal;
using Council.Dev.Validation;

namespace Council.Dev.Execution;

public sealed class AutonomousDevelopmentExecutor : IDevelopmentExecutor
{
    private readonly IDevelopmentPlanner _planner;
    private readonly DevelopmentActionExecutor _actionExecutor;
    private readonly BuildTestExecutor _buildTestExecutor;
    private readonly TaskManager _taskManager;

    public AutonomousDevelopmentExecutor(
        IDevelopmentPlanner planner,
        DevelopmentActionExecutor actionExecutor,
        BuildTestExecutor buildTestExecutor,
        TaskManager taskManager)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(actionExecutor);
        ArgumentNullException.ThrowIfNull(buildTestExecutor);
        ArgumentNullException.ThrowIfNull(taskManager);

        _planner = planner;
        _actionExecutor = actionExecutor;
        _buildTestExecutor = buildTestExecutor;
        _taskManager = taskManager;
    }

    public async Task<DevelopmentExecutionResult> ExecuteAsync(
        DevelopmentTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        cancellationToken.ThrowIfCancellationRequested();

        await _taskManager.UpdateStepAsync(
            task.Id,
            "Creating AI development plan.",
            CancellationToken.None);

        var plan = await _planner.CreatePlanAsync(
            task,
            cancellationToken);

        if (!plan.Success)
        {
            return DevelopmentExecutionResult.Failed(
                $"Planning failed: {plan.Summary}");
        }

        var initialResult = await ExecutePlanAsync(
            task,
            plan,
            "Initial development plan",
            cancellationToken);

        if (initialResult.Success)
        {
            return initialResult;
        }

        cancellationToken.ThrowIfCancellationRequested();

        await _taskManager.UpdateStepAsync(
            task.Id,
            "Initial development attempt failed. Creating AI recovery plan.",
            CancellationToken.None);

        var recoveryPlan = await _planner.CreateRecoveryPlanAsync(
            task,
            initialResult.Message,
            cancellationToken);

        if (!recoveryPlan.Success)
        {
            return DevelopmentExecutionResult.Failed(
                initialResult.Message +
                $" | Recovery planning failed: {recoveryPlan.Summary}");
        }

        await _taskManager.UpdateStepAsync(
            task.Id,
            $"Recovery plan created: {recoveryPlan.Summary}",
            CancellationToken.None);

        var recoveryResult = await ExecutePlanAsync(
            task,
            recoveryPlan,
            "Recovery plan",
            cancellationToken);

        if (recoveryResult.Success)
        {
            return DevelopmentExecutionResult.Completed(
                "Initial development plan required recovery. " +
                recoveryResult.Message);
        }

        return DevelopmentExecutionResult.Failed(
            initialResult.Message +
            $" | Recovery failed: {recoveryResult.Message}");
    }

    private async Task<DevelopmentExecutionResult> ExecutePlanAsync(
        DevelopmentTask task,
        DevelopmentPlan plan,
        string planName,
        CancellationToken cancellationToken)
    {
        if (plan.Actions.Count == 0)
        {
            return DevelopmentExecutionResult.Failed(
                $"{planName} contains no actions.");
        }

        await _taskManager.UpdateStepAsync(
            task.Id,
            $"{planName} created: {plan.Summary}",
            CancellationToken.None);

        for (var index = 0; index < plan.Actions.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var action = plan.Actions[index];

            await _taskManager.UpdateStepAsync(
                task.Id,
                $"{planName}: executing action " +
                $"{index + 1} of {plan.Actions.Count}: " +
                $"{action.Type} -> {action.Target}",
                CancellationToken.None);

            var result = await _actionExecutor.ExecuteAsync(
                action,
                cancellationToken);

            if (!result.Success)
            {
                var failure =
                    $"Action {index + 1} failed: {result.Message}" +
                    (string.IsNullOrWhiteSpace(result.Content)
                        ? string.Empty
                        : $" | {result.Content}");

                await _taskManager.UpdateStepAsync(
                    task.Id,
                    failure,
                    CancellationToken.None);

                return DevelopmentExecutionResult.Failed(
                    failure);
            }

            await _taskManager.CompleteStepAsync(
                task.Id,
                $"{action.Type} -> {action.Target}",
                CancellationToken.None);
        }

        await _taskManager.UpdateStepAsync(
            task.Id,
            $"{planName}: all actions completed. " +
            "Running build and tests.",
            CancellationToken.None);

        var validation = await _buildTestExecutor.RunAsync(
            cancellationToken);

        if (!validation.Success)
        {
            var failure = BuildValidationFailureMessage(
                validation);

            await _taskManager.UpdateStepAsync(
                task.Id,
                failure,
                CancellationToken.None);

            return DevelopmentExecutionResult.Failed(
                failure);
        }

        await _taskManager.UpdateStepAsync(
            task.Id,
            $"{planName}: validation passed.",
            CancellationToken.None);

        return DevelopmentExecutionResult.Completed(
            $"{planName} completed successfully. " +
            $"Executed {plan.Actions.Count} actions. " +
            $"{validation.Message}");
    }

    private static string BuildValidationFailureMessage(
        ValidationResult validation)
    {
        var message = validation.Message;

        if (validation.BuildResult is not null)
        {
            message +=
                Environment.NewLine +
                "Build exit code: " +
                validation.BuildResult.ExitCode;

            if (!string.IsNullOrWhiteSpace(
                    validation.BuildResult.StandardOutput))
            {
                message +=
                    Environment.NewLine +
                    "Build output:" +
                    Environment.NewLine +
                    validation.BuildResult.StandardOutput;
            }

            if (!string.IsNullOrWhiteSpace(
                    validation.BuildResult.StandardError))
            {
                message +=
                    Environment.NewLine +
                    "Build error:" +
                    Environment.NewLine +
                    validation.BuildResult.StandardError;
            }
        }

        if (validation.TestResult is not null)
        {
            message +=
                Environment.NewLine +
                "Test exit code: " +
                validation.TestResult.ExitCode;

            if (!string.IsNullOrWhiteSpace(
                    validation.TestResult.StandardOutput))
            {
                message +=
                    Environment.NewLine +
                    "Test output:" +
                    Environment.NewLine +
                    validation.TestResult.StandardOutput;
            }

            if (!string.IsNullOrWhiteSpace(
                    validation.TestResult.StandardError))
            {
                message +=
                    Environment.NewLine +
                    "Test error:" +
                    Environment.NewLine +
                    validation.TestResult.StandardError;
            }
        }

        if (validation.PythonValidationResult is not null)
        {
            message +=
                Environment.NewLine +
                "Python validation exit code: " +
                validation.PythonValidationResult.ExitCode;

            if (!string.IsNullOrWhiteSpace(validation.PythonValidationResult.StandardOutput))
                message += Environment.NewLine + "Python validation output:" +
                    Environment.NewLine + validation.PythonValidationResult.StandardOutput;

            if (!string.IsNullOrWhiteSpace(validation.PythonValidationResult.StandardError))
                message += Environment.NewLine + "Python validation error:" +
                    Environment.NewLine + validation.PythonValidationResult.StandardError;
        }

        return message;
    }
}

