using System.Text;

namespace Council.App.Office;

/// <summary>
/// Loads focused Office workflow instructions from the local Skills/Office directory.
/// Skill files are guidance, not executable code or model fine-tuning.
/// </summary>
public sealed class OfficeSkillLibrary
{
    private readonly string _skillDirectory;

    public OfficeSkillLibrary(string? skillDirectory = null)
    {
        _skillDirectory = skillDirectory ??
            Environment.GetEnvironmentVariable("JARVIS_SKILLS_PATH") ??
            Path.Combine(AppContext.BaseDirectory, "Skills", "Office");
    }

    public string? GetInstructions(string userRequest)
    {
        var skill = SelectSkill(userRequest);
        if (skill is null)
            return null;

        var path = Path.Combine(_skillDirectory, skill + ".md");
        if (!File.Exists(path))
            return null;

        var content = File.ReadAllText(path);
        return "Relevant local workflow skill: " + skill + Environment.NewLine +
               "Follow these instructions, while respecting the registered tools and their actual capabilities:" +
               Environment.NewLine + content;
    }

    public static string? SelectSkill(string userRequest)
    {
        if (string.IsNullOrWhiteSpace(userRequest))
            return null;

        var text = userRequest.ToLowerInvariant();
        if (ContainsAny(text, ".xlsx", ".xlsm", ".csv", "excel", "spreadsheet", "workbook", "formula", "worksheet", "pivot table", "chart"))
            return "excel-specialist";
        if (ContainsAny(text, ".docx", ".dotx", "word document", "word file", "thesis", "report", "memo", "letter", "track changes"))
            return "word-specialist";
        if (ContainsAny(text, ".pptx", ".potx", "powerpoint", "presentation", "slides", "slide deck"))
            return "powerpoint-specialist";
        if (ContainsAny(text, ".pdf", "pdf file", "pdf document", "scanned pdf", "ocr"))
            return "pdf-specialist";
        if (ContainsAny(text, "microsoft office", "office document", "office file"))
            return "office-general";

        return null;
    }

    private static bool ContainsAny(string text, params string[] terms) =>
        terms.Any(term => text.Contains(term, StringComparison.OrdinalIgnoreCase));
}
