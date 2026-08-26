namespace Council.Dev.Planning;

public sealed record RepositoryContext(
    string WorkingDirectory,
    IReadOnlyList<string> Files,
    IReadOnlyList<string> Projects)
{
    public string ToPromptText()
    {
        var text = new System.Text.StringBuilder();

        text.AppendLine("Repository root:");
        text.AppendLine(WorkingDirectory);
        text.AppendLine();

        text.AppendLine("Projects:");
        foreach (var project in Projects)
        {
            text.AppendLine($"- {project}");
        }

        text.AppendLine();

        text.AppendLine("Repository files:");
        foreach (var file in Files)
        {
            text.AppendLine($"- {file}");
        }

        return text.ToString();
    }
}
