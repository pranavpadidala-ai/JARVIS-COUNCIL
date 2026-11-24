using ClosedXML.Excel;
using Council.App.Office;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Xunit;

namespace Council.Dev.Tests;

public sealed class OfficeEditorToolTests : IDisposable
{
    private readonly string _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "jarvis-office-" + Guid.NewGuid().ToString("N"))).FullName;
    private readonly OfficeEditorTool _tool;

    public OfficeEditorToolTests() => _tool = new OfficeEditorTool(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task WritesLiveFormulasAndExpertFormatting()
    {
        var result = await _tool.ExecuteAsync(new()
        {
            ["action"] = "excel_write",
            ["output"] = "beam check.xlsx",
            ["sheet"] = "Calc",
            ["cells"] = "{\"A1\":\"Load (kN)\",\"B1\":\"Capacity (kN)\",\"C1\":\"Utilisation\",\"D1\":\"Grade\",\"A2\":120,\"B2\":150,\"C2\":\"=A2/B2\",\"A3\":180,\"B3\":150,\"C3\":\"=A3/B3\"}",
            ["formats"] = "{\"A1:D1\":{\"bold\":true,\"fill\":\"#DDEBF7\",\"border\":true},\"C2:C3\":{\"number_format\":\"0.00\"}}",
            ["column_widths"] = "auto",
            ["freeze"] = "A2",
            ["autofilter"] = "A1:D3",
            ["validation"] = "[{\"range\":\"D2:D3\",\"list\":[\"Fe250\",\"Fe410\"]}]",
            ["highlight"] = "[{\"range\":\"C2:C3\",\"when\":\"greater_than\",\"value\":1}]"
        });
        Assert.True(result.Success, result.Message);
        Assert.Contains("2 live formula(s)", result.Message);

        using var wb = new XLWorkbook(Path.Combine(_root, "beam check.xlsx"));
        var ws = wb.Worksheet("Calc");
        Assert.Equal("A3/B3", ws.Cell("C3").FormulaA1);
        Assert.Equal(1.2, ws.Cell("C3").Value.GetNumber(), 3);
        Assert.True(ws.Cell("A1").Style.Font.Bold);
        Assert.Equal("0.00", ws.Cell("C2").Style.NumberFormat.Format);
        Assert.Equal(1, ws.SheetView.SplitRow);
        Assert.True(ws.AutoFilter.IsEnabled);
        Assert.Single(ws.DataValidations);
        Assert.Single(ws.ConditionalFormats);
    }

    [Fact]
    public async Task NeverOverwritesAndStaysInsideTheWorkspace()
    {
        File.WriteAllText(Path.Combine(_root, "taken.xlsx"), "x");
        Assert.False((await _tool.ExecuteAsync(new() { ["action"] = "excel_write", ["output"] = "taken.xlsx", ["cells"] = "{\"A1\":1}" })).Success);
        Assert.False((await _tool.ExecuteAsync(new() { ["action"] = "excel_write", ["output"] = "..\\escape.xlsx", ["cells"] = "{\"A1\":1}" })).Success);
        Assert.False((await _tool.ExecuteAsync(new() { ["action"] = "excel_write", ["output"] = "bad.xlsx", ["cells"] = "{\"Z\":1}" })).Success);
    }

    [Fact]
    public async Task BuildsAReportWithRealHeadingStylesAndAToc()
    {
        var result = await _tool.ExecuteAsync(new()
        {
            ["action"] = "word_build",
            ["output"] = "report.docx",
            ["title"] = "HAZ study",
            ["blocks"] = "[{\"type\":\"toc\"},{\"type\":\"heading\",\"level\":1,\"text\":\"Introduction\"},{\"type\":\"paragraph\",\"text\":\"Aluminium welds soften.\"}," +
                         "{\"type\":\"bullets\",\"items\":[\"6061-T6\",\"6082-T6\"]},{\"type\":\"table\",\"caption\":\"Table 1: Alloys\",\"rows\":[[\"Alloy\",\"fo (MPa)\"],[\"6082-T6\",\"260\"]]}," +
                         "{\"type\":\"heading\",\"level\":2,\"text\":\"Scope\"}]"
        });
        Assert.True(result.Success, result.Message);

        using var doc = WordprocessingDocument.Open(Path.Combine(_root, "report.docx"), false);
        var main = doc.MainDocumentPart!;
        var styleIds = main.Document.Body!.Descendants<ParagraphStyleId>().Select(s => s.Val!.Value).ToList();
        Assert.Contains("Heading1", styleIds);
        Assert.Contains("Heading2", styleIds);
        Assert.Contains("Caption", styleIds);
        Assert.Contains(main.StyleDefinitionsPart!.Styles!.Elements<Style>(), s => s.StyleId == "Heading1" && s.Descendants<OutlineLevel>().Any());
        Assert.Contains(main.Document.Descendants<FieldCode>(), f => f.Text.Contains("TOC"));
        Assert.Single(main.Document.Descendants<Table>());
        Assert.NotNull(main.DocumentSettingsPart!.Settings!.GetFirstChild<UpdateFieldsOnOpen>());
        var problems = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).Select(e => e.Description).ToList();
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact]
    public async Task EditsACopyWithReplaceAcrossRunsAndAppend()
    {
        await _tool.ExecuteAsync(new() { ["action"] = "word_build", ["output"] = "draft.docx", ["blocks"] = "[{\"type\":\"paragraph\",\"text\":\"The HAZ width is 25 mm.\"}]" });
        // Split the sentence across runs like Word often does.
        using (var doc = WordprocessingDocument.Open(Path.Combine(_root, "draft.docx"), true))
        {
            var paragraph = doc.MainDocumentPart!.Document.Body!.Elements<Paragraph>().First();
            paragraph.RemoveAllChildren<Run>();
            paragraph.Append(new Run(new Text("The HAZ wid") { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve }), new Run(new Text("th is 25 mm.")));
            doc.MainDocumentPart.Document.Save();
        }

        var result = await _tool.ExecuteAsync(new()
        {
            ["action"] = "word_edit",
            ["path"] = "draft.docx",
            ["output"] = "draft v2.docx",
            ["replace"] = "[{\"find\":\"HAZ width\",\"replace\":\"heat-affected zone width\"},{\"find\":\"25 mm\",\"replace\":\"30 mm\"}]",
            ["append"] = "[{\"type\":\"heading\",\"level\":1,\"text\":\"Conclusions\"}]"
        });
        Assert.True(result.Success, result.Message);
        Assert.Contains("'HAZ width' -> 'heat-affected zone width': 1", result.Message);

        using var edited = WordprocessingDocument.Open(Path.Combine(_root, "draft v2.docx"), false);
        var text = edited.MainDocumentPart!.Document.Body!.InnerText;
        Assert.Contains("The heat-affected zone width is 30 mm.", text);
        Assert.Contains("Conclusions", text);

        using var original = WordprocessingDocument.Open(Path.Combine(_root, "draft.docx"), false);
        Assert.Contains("25 mm", original.MainDocumentPart!.Document.Body!.InnerText);
    }
}
