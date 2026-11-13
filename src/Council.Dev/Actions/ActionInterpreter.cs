namespace Council.Dev.Actions;

public sealed class ActionInterpreter
{
    public IReadOnlyList<DevelopmentAction> Interpret(
        IReadOnlyList<string> steps)
    {
        ArgumentNullException.ThrowIfNull(steps);

        var actions = new List<DevelopmentAction>();

        foreach (var step in steps)
        {
            if (string.IsNullOrWhiteSpace(step))
            {
                continue;
            }

            var normalized = step.Trim();

            if (LooksLikeBuild(normalized))
            {
                actions.Add(
                    new DevelopmentAction(
                        DevelopmentActionType.RunBuild,
                        "."));

                continue;
            }

            if (LooksLikeTests(normalized))
            {
                actions.Add(
                    new DevelopmentAction(
                        DevelopmentActionType.RunTests,
                        "."));

                continue;
            }

            if (LooksLikeListFiles(normalized))
            {
                actions.Add(
                    new DevelopmentAction(
                        DevelopmentActionType.ListFiles,
                        ExtractDirectoryTarget(normalized)));

                continue;
            }

            if (LooksLikeSearch(normalized))
            {
                actions.Add(
                    new DevelopmentAction(
                        DevelopmentActionType.SearchCode,
                        ExtractSearchTarget(normalized)));

                continue;
            }

            if (LooksLikeRead(normalized))
            {
                actions.Add(
                    new DevelopmentAction(
                        DevelopmentActionType.ReadFile,
                        ExtractPath(normalized)));

                continue;
            }

            if (LooksLikeCreate(normalized))
            {
                actions.Add(
                    new DevelopmentAction(
                        DevelopmentActionType.CreateFile,
                        ExtractPath(normalized)));

                continue;
            }

            if (LooksLikeModify(normalized))
            {
                actions.Add(
                    new DevelopmentAction(
                        DevelopmentActionType.ModifyFile,
                        ExtractPath(normalized)));

                continue;
            }

            if (LooksLikeDelete(normalized))
            {
                actions.Add(
                    new DevelopmentAction(
                        DevelopmentActionType.DeleteFile,
                        ExtractPath(normalized)));

                continue;
            }

            // Unknown actions are deliberately not converted
            // into executable operations.
            actions.Add(
                new DevelopmentAction(
                    DevelopmentActionType.SearchCode,
                    normalized));
        }

        return actions;
    }

    private static bool LooksLikeBuild(string step)
    {
        return step.Contains(
                   "dotnet build",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "build the project",
                   StringComparison.OrdinalIgnoreCase)
               || step.Equals(
                   "build",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeTests(string step)
    {
        return step.Contains(
                   "dotnet test",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "run tests",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "run the tests",
                   StringComparison.OrdinalIgnoreCase)
               || step.Equals(
                   "test",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractDirectoryTarget(string step)
    {
        var path = ExtractPath(step);

        return string.Equals(
            path,
            step,
            StringComparison.OrdinalIgnoreCase)
            ? "."
            : path;
    }

    private static bool LooksLikeListFiles(string step)
    {
        return step.Contains("list files", StringComparison.OrdinalIgnoreCase)
               || step.Contains("show files", StringComparison.OrdinalIgnoreCase)
               || step.Contains("list directory", StringComparison.OrdinalIgnoreCase)
               || step.Contains("directory tree", StringComparison.OrdinalIgnoreCase)
               || step.Equals("list", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeSearch(string step)
    {
        return step.Contains(
                   "search",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "find",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "inspect the existing",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeRead(string step)
    {
        return step.Contains(
                   "read ",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "open ",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "inspect ",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeCreate(string step)
    {
        return step.Contains(
                   "create ",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "add new file",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "new file",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeModify(string step)
    {
        return step.Contains(
                   "modify ",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "update ",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "change ",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "edit ",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static bool LooksLikeDelete(string step)
    {
        return step.Contains(
                   "delete ",
                   StringComparison.OrdinalIgnoreCase)
               || step.Contains(
                   "remove ",
                   StringComparison.OrdinalIgnoreCase);
    }

    private static string ExtractPath(string step)
    {
        var tokens = step
            .Split(
                [' ', '\t', '\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries);

        foreach (var token in tokens)
        {
            var cleaned = token
                .Trim(
                    '"',
                    '\'',
                    '`',
                    '.',
                    ':',
                    ',',
                    ';',
                    '(',
                    ')');

            if (LooksLikePath(cleaned))
            {
                return cleaned;
            }
        }

        return step;
    }

    private static string ExtractSearchTarget(string step)
    {
        var index = step.IndexOf(
            "search",
            StringComparison.OrdinalIgnoreCase);

        if (index >= 0)
        {
            var target = step[(index + "search".Length)..]
                .Trim(
                    ' ',
                    ':',
                    '"',
                    '\'',
                    '`');

            if (!string.IsNullOrWhiteSpace(target))
            {
                return target;
            }
        }

        return step;
    }

    private static bool LooksLikePath(string value)
    {
        return value.EndsWith(
                   ".cs",
                   StringComparison.OrdinalIgnoreCase)
               || value.EndsWith(
                   ".csproj",
                   StringComparison.OrdinalIgnoreCase)
               || value.EndsWith(
                   ".json",
                   StringComparison.OrdinalIgnoreCase)
               || value.EndsWith(
                   ".xml",
                   StringComparison.OrdinalIgnoreCase)
               || value.EndsWith(
                   ".md",
                   StringComparison.OrdinalIgnoreCase)
               || value.Contains(
                   '/',
                   StringComparison.Ordinal)
               || value.Contains(
                   '\\',
                   StringComparison.Ordinal);
    }
}