using System.Text;
using System.Text.Json;
using Council.Core.Models;
using Council.Core.Providers;
using Council.Dev.Actions;
using Council.Dev.Tasks;

namespace Council.Dev.Planning;

public sealed class AIDevelopmentPlanner : IDevelopmentPlanner
{
    private readonly IAIProvider _aiProvider;
    private readonly RepositoryContextProvider _repositoryContextProvider;

    public AIDevelopmentPlanner(
        IAIProvider aiProvider,
        RepositoryContextProvider repositoryContextProvider)
    {
        ArgumentNullException.ThrowIfNull(aiProvider);
        ArgumentNullException.ThrowIfNull(repositoryContextProvider);

        _aiProvider = aiProvider;
        _repositoryContextProvider = repositoryContextProvider;
    }

    public async Task<DevelopmentPlan> CreatePlanAsync(
        DevelopmentTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        cancellationToken.ThrowIfCancellationRequested();

        var repositoryContext =
            await _repositoryContextProvider.InspectAsync(
                cancellationToken);

        var prompt =
            BuildPlanningPrompt(
                task,
                repositoryContext);

        return await RequestPlanAsync(
            prompt,
            cancellationToken);
    }

    public async Task<DevelopmentPlan> CreateRecoveryPlanAsync(
        DevelopmentTask task,
        string failure,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        if (string.IsNullOrWhiteSpace(failure))
        {
            return DevelopmentPlan.Failed(
                "Recovery failure information is empty.");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var repositoryContext =
            await _repositoryContextProvider.InspectAsync(
                cancellationToken);

        var prompt =
            BuildRecoveryPrompt(
                task,
                failure,
                repositoryContext);

        return await RequestPlanAsync(
            prompt,
            cancellationToken);
    }

    private static string BuildPlanningPrompt(
        DevelopmentTask task,
        RepositoryContext context)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "You are the planning component of an autonomous software development system.");
        builder.AppendLine();

        builder.AppendLine(
            "Create a safe, practical implementation plan for this task.");
        builder.AppendLine();

        builder.AppendLine("TASK TITLE:");
        builder.AppendLine(task.Title);
        builder.AppendLine();

        builder.AppendLine("TASK DESCRIPTION:");
        builder.AppendLine(task.Description);
        builder.AppendLine();

        AppendCheckpointContext(builder, task);

        builder.AppendLine("ACTUAL REPOSITORY CONTEXT:");
        builder.AppendLine(context.ToPromptText());
        builder.AppendLine();

        AppendPlanRules(builder);

        return builder.ToString();
    }

    private static string BuildRecoveryPrompt(
        DevelopmentTask task,
        string failure,
        RepositoryContext context)
    {
        var builder = new StringBuilder();

        builder.AppendLine(
            "You are the recovery planner of an autonomous software development system.");
        builder.AppendLine();

        builder.AppendLine(
            "The previous development attempt failed.");
        builder.AppendLine(
            "Diagnose the failure and create a corrected implementation plan.");
        builder.AppendLine();

        builder.AppendLine("TASK TITLE:");
        builder.AppendLine(task.Title);
        builder.AppendLine();

        builder.AppendLine("TASK DESCRIPTION:");
        builder.AppendLine(task.Description);
        builder.AppendLine();

        AppendCheckpointContext(builder, task);

        builder.AppendLine("PREVIOUS FAILURE:");
        builder.AppendLine(failure);
        builder.AppendLine();

        builder.AppendLine("CURRENT REPOSITORY CONTEXT:");
        builder.AppendLine(context.ToPromptText());
        builder.AppendLine();

        builder.AppendLine(
            "IMPORTANT RECOVERY RULES:");
        builder.AppendLine(
            "- Diagnose the previous failure.");
        builder.AppendLine(
            "- Do not blindly repeat the failed action.");
        builder.AppendLine(
            "- Use the actual repository context above.");
        builder.AppendLine(
            "- Do not assume files exist when they are not listed.");
        builder.AppendLine(
            "- Use repository-relative paths.");
        builder.AppendLine();

        AppendPlanRules(builder);

        return builder.ToString();
    }

    private static void AppendCheckpointContext(StringBuilder builder, DevelopmentTask task)
    {
        builder.AppendLine("DURABLE TASK CHECKPOINT:");
        builder.AppendLine("Task idempotency key: " + task.IdempotencyKey);
        builder.AppendLine("Completed steps (do not repeat unless repository inspection proves they did not take effect):");
        if (task.CompletedSteps.Count == 0)
            builder.AppendLine("- None recorded.");
        else
            foreach (var step in task.CompletedSteps)
                builder.AppendLine("- " + step);
        builder.AppendLine("Exact last recorded step / next action:");
        builder.AppendLine(string.IsNullOrWhiteSpace(task.CurrentStep) ? "- No next action recorded." : task.CurrentStep);
        builder.AppendLine("Recorded provider route: " + (task.ProviderRoute ?? "unknown"));
        builder.AppendLine("Recorded Git baseline: " + (task.GitCommitSha ?? "unknown"));
        builder.AppendLine("Recorded working-tree state: " + (task.GitWorkingTreeStatus ?? "unknown"));
        builder.AppendLine("Recovery instructions:");
        builder.AppendLine("- Resume from the checkpoint; do not restart completed work from scratch.");
        builder.AppendLine("- Inspect current files and Git state before deciding whether the last action took effect.");
        builder.AppendLine("- Reconcile ambiguous operations before retrying; avoid duplicate side effects.");
        builder.AppendLine("- If the actual repository conflicts with the checkpoint, report the discrepancy and plan a safe reconciliation.");
        builder.AppendLine();
    }

    private static void AppendPlanRules(
        StringBuilder builder)
    {
        builder.AppendLine(
            "Return ONLY valid JSON.");
        builder.AppendLine(
            "Do not use markdown fences.");
        builder.AppendLine(
            "Do not include explanations outside the JSON.");
        builder.AppendLine();

        builder.AppendLine("Required structure:");
        builder.AppendLine("{");
        builder.AppendLine("  \"summary\": \"short summary\",");
        builder.AppendLine("  \"actions\": [");
        builder.AppendLine("    {");
        builder.AppendLine("      \"type\": \"ReadFile\",");
        builder.AppendLine("      \"target\": \"path or target\",");
        builder.AppendLine("      \"content\": null");
        builder.AppendLine("    }");
        builder.AppendLine("  ]");
        builder.AppendLine("}");
        builder.AppendLine();

        builder.AppendLine("Allowed action types are ONLY:");
        builder.AppendLine("ReadFile");
        builder.AppendLine("SearchCode");
        builder.AppendLine("CreateFile");
        builder.AppendLine("ModifyFile");
        builder.AppendLine("DeleteFile");
        builder.AppendLine("RunBuild");
        builder.AppendLine("RunTests");
        builder.AppendLine("GitDiff");
        builder.AppendLine();

        builder.AppendLine("Rules:");
        builder.AppendLine(
            "- Do not execute commands directly.");
        builder.AppendLine(
            "- Do not use shell commands.");
        builder.AppendLine(
            "- Do not modify files directly.");
        builder.AppendLine(
            "- Keep actions ordered.");
        builder.AppendLine(
            "- Use only repository-relative paths where applicable.");
        builder.AppendLine(
            "- Prefer SearchCode when the exact file/path is unknown.");
        builder.AppendLine(
            "- Include build/test validation where appropriate.");
    }

    private async Task<DevelopmentPlan> RequestPlanAsync(
        string prompt,
        CancellationToken cancellationToken)
    {
        var request = new AIRequest(
            Messages:
            [
                new AIMessage(
                    "user",
                    prompt)
            ],
            Temperature: 0.1);

        var response = await _aiProvider.AskAsync(
            request,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(response.Content))
        {
            return DevelopmentPlan.Failed(
                "AI returned an empty development plan.");
        }

        var json = ExtractJson(response.Content);

        if (string.IsNullOrWhiteSpace(json))
        {
            return DevelopmentPlan.Failed(
                "AI returned invalid plan JSON.");
        }

        try
        {
            var parsed =
                JsonSerializer.Deserialize<PlanPayload>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

            if (parsed is null ||
                string.IsNullOrWhiteSpace(parsed.Summary) ||
                parsed.Actions is null ||
                parsed.Actions.Count == 0)
            {
                return DevelopmentPlan.Failed(
                    "AI returned an invalid development plan.");
            }

            var actions = new List<DevelopmentAction>();

            foreach (var action in parsed.Actions)
            {
                if (!Enum.TryParse<DevelopmentActionType>(
                        action.Type,
                        true,
                        out var actionType))
                {
                    return DevelopmentPlan.Failed(
                        $"AI returned unsupported action type: {action.Type}");
                }

                if (string.IsNullOrWhiteSpace(action.Target))
                {
                    return DevelopmentPlan.Failed(
                        "AI returned an action without a target.");
                }

                actions.Add(
                    new DevelopmentAction(
                        actionType,
                        action.Target,
                        action.Content));
            }

            return DevelopmentPlan.Created(
                parsed.Summary,
                actions);
        }
        catch (JsonException)
        {
            return DevelopmentPlan.Failed(
                "AI returned invalid plan JSON.");
        }
    }

    private static string? ExtractJson(string content)
    {
        var text = content.Trim();

        if (text.StartsWith("`", StringComparison.Ordinal))
        {
            var firstNewLine = text.IndexOf('\n');

            if (firstNewLine >= 0)
            {
                text = text[(firstNewLine + 1)..];
            }

            var closingFence = text.LastIndexOf(
                "`",
                StringComparison.Ordinal);

            if (closingFence >= 0)
            {
                text = text[..closingFence];
            }

            text = text.Trim();
        }

        var firstBrace = text.IndexOf('{');
        var lastBrace = text.LastIndexOf('}');

        if (firstBrace < 0 ||
            lastBrace <= firstBrace)
        {
            return null;
        }

        return text[firstBrace..(lastBrace + 1)];
    }

    private sealed record PlanPayload(
        string Summary,
        List<ActionPayload> Actions);

    private sealed record ActionPayload(
        string Type,
        string Target,
        string? Content);
}
