using Council.App.Office;

namespace Council.Dev.Tests;

public sealed class OfficeSkillLibraryTests
{
    [Theory]
    [InlineData("Please fix the formulas in this Excel workbook.xlsx", "excel-specialist")]
    [InlineData("Create a thesis report in Word", "word-specialist")]
    [InlineData("Make a PowerPoint presentation", "powerpoint-specialist")]
    [InlineData("Extract tables from this scanned PDF", "pdf-specialist")]
    [InlineData("Help me with a Microsoft Office document", "office-general")]
    [InlineData("What is the weather today?", null)]
    public void SelectSkill_ReturnsRelevantSkill(string request, string? expected) =>
        Assert.Equal(expected, OfficeSkillLibrary.SelectSkill(request));

    [Fact]
    public void GetInstructions_LoadsSkillFromConfiguredDirectory()
    {
        var root = Path.Combine(Path.GetTempPath(), "JarvisSkills", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "excel-specialist.md"), "Excel test instructions");
            var library = new OfficeSkillLibrary(root);
            Assert.Contains("Excel test instructions", library.GetInstructions("Edit my Excel workbook")!);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
