using System.Text.Json;
using Council.Core.Models;
using Council.Core.Providers;
using Council.Dev.Actions;
using Council.Dev.Tasks;

namespace Council.Dev.Planning;

public sealed class AIDevelopmentPlanner : IDevelopmentPlanner
{
    private readonly IAIProvider _aiProvider;

    public AIDevelopmentPlanner(IAIProvider aiProvider)
    {
        ArgumentNullException.ThrowIfNull(aiProvider);

        _aiProvider = aiProvider;
    }

    public async Task<DevelopmentPlan> CreatePlanAsync(
        DevelopmentTask task,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(task);

        cancellationToken.ThrowIfCancellationRequested();

        var prompt =
            "You are the planning component of an autonomous software " +
            "development system.\n\n" +
            "Create a safe, practical implementation plan for this task.\n\n" +
            $"Task title:\n{task.Title}\n\n" +
            $"Task description:\n{task.Description}\n\n" +
            "Return ONLY valid JSON. Do not use markdown fences. " +
            "Do not include explanations before or after the JSON.\n\n" +
            "Required structure:\n" +
            "{\n" +
            "  \"summary\": \"short summary\",\n" +
            "  \"actions\": [\n" +
            "    {\n" +
            "      \"type\": \"ReadFile\",\n" +
            "      \"target\": \"path or target\",\n" +
            "      \"content\": null\n" +
            "    }\n" +
            "  ]\n" +
            "}\n\n" +
            "Allowed action types are ONLY:\n" +
            "ReadFile\n" +
            "SearchCode\n" +
            "CreateFile\n" +
            "ModifyFile\n" +
            "DeleteFile\n" +
            "RunBuild\n" +
            "RunTests\n" +
            "GitDiff\n\n" +
            "Rules:\n" +
            "- Do not execute commands directly.\n" +
            "- Do not use shell commands.\n" +
            "- Do not modify files directly.\n" +
            "- Use only the allowed action types.\n" +
            "- Keep actions ordered.\n" +
            "- Use repository-relative paths where applicable.\n" +
            "- Do not assume files exist unless known from the task/context.\n" +
            "- Prefer SearchCode when the exact file/path is unknown.\n" +
            "- Include build/test validation where appropriate.";

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

        var prompt =
            "You are the recovery planner of an autonomous software " +
            "development system.\n\n" +
            "The previous development attempt failed. " +
            "Create a corrected plan that avoids repeating the failure.\n\n" +
            $"Task title:\n{task.Title}\n\n" +
            $"Task description:\n{task.Description}\n\n" +
            $"Previous failure:\n{failure}\n\n" +
            "IMPORTANT:\n" +
            "- Diagnose the failure.\n" +
            "- Do not repeat the failed action unless you have a valid reason.\n" +
            "- If a file does not exist, search the repository instead.\n" +
            "- Do not assume README.md or any other file exists.\n" +
            "- Use repository-relative paths.\n" +
            "- Return ONLY valid JSON.\n" +
            "- Do not use markdown fences.\n" +
            "- Do not include explanations outside the JSON.\n\n" +
            "Required structure:\n" +
            "{\n" +
            "  \"summary\": \"short recovery summary\",\n" +
            "  \"actions\": [\n" +
            "    {\n" +
            "      \"type\": \"SearchCode\",\n" +
            "      \"target\": \"pattern\",\n" +
            "      \"content\": null\n" +
            "    }\n" +
            "  ]\n" +
            "}\n\n" +
            "Allowed action types are ONLY:\n" +
            "ReadFile\n" +
            "SearchCode\n" +
            "CreateFile\n" +
            "ModifyFile\n" +
            "DeleteFile\n" +
            "RunBuild\n" +
            "RunTests\n" +
            "GitDiff";

        return await RequestPlanAsync(
            prompt,
            cancellationToken);
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
                        ignoreCase: true,
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

        if (firstBrace < 0 || lastBrace <= firstBrace)
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



