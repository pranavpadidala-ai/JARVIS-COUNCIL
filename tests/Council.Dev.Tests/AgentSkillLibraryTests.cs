using Council.App.Skills;

namespace Council.Dev.Tests;

public sealed class AgentSkillLibraryTests
{
    [Fact]
    public void DiscoversSkillsAndSelectsRelevantGuidance()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisAgentSkills", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(root, "Research"));
        try
        {
            File.WriteAllText(Path.Combine(root, "Research", "SKILL.md"),
                "---\nname: research-assistant\ndescription: Evidence-based research\ntriggers: literature review, research paper, academic sources\n---\n# Research workflow\nCheck source quality.");
            var library = new AgentSkillLibrary(root);
            Assert.Contains("research-assistant", library.ListSkills());
            var instructions = library.GetInstructions("Help me conduct a literature review");
            Assert.NotNull(instructions);
            Assert.Contains("Check source quality", instructions);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ReturnsNullWhenNoSkillMatches()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisAgentSkills", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "SKILL.md"),
                "---\nname: excel-specialist\ndescription: Spreadsheet workflows\ntriggers: excel, spreadsheet\n---\nExcel only.");
            Assert.Null(new AgentSkillLibrary(root).GetInstructions("Tell me a joke"));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
