using System.Text;
using ClosedXML.Excel;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Council.App.Office;
using Council.Execution.Tools;

namespace Council.Dev.Tests;

public sealed class OfficeWorkspaceToolTests
{
    [Fact]
    public async Task InspectDocument_ReadsExcelSheetAndValues()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            using (var workbook = new XLWorkbook())
            {
                var sheet = workbook.Worksheets.Add("Results");
                sheet.Cell("A1").Value = "Load";
                sheet.Cell("B1").Value = "Displacement";
                sheet.Cell("A2").Value = 125.5;
                sheet.Cell("B2").Value = 4.2;
                workbook.SaveAs(Path.Combine(root, "results.xlsx"));
            }

            var tool = new OfficeWorkspaceTool(root);
            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "inspect_document",
                ["path"] = "results.xlsx"
            });

            Assert.True(result.Success);
            Assert.Contains("Results", result.Message);
            Assert.Contains("125.5", result.Message);
            Assert.Contains("4.2", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InspectDocument_ExtractsSelectablePdfText()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            WriteMinimalPdf(Path.Combine(root, "paper.pdf"));
            var tool = new OfficeWorkspaceTool(root);

            var listing = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "list_documents"
            });
            Assert.True(listing.Success);
            Assert.Contains("paper.pdf", listing.Message);

            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "inspect_document",
                ["path"] = "paper.pdf"
            });

            Assert.True(result.Success, result.Message);
            Assert.Contains("Page 1", result.Message);
            Assert.Contains("JARVIS PDF extraction test", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzePaper_ExtractsPageLabelledTextInQuickMode()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            WriteMinimalPdf(Path.Combine(root, "paper.pdf"));
            var result = await new OfficeWorkspaceTool(root).ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "analyze_paper",
                ["path"] = "paper.pdf"
            });

            Assert.True(result.Success, result.Message);
            Assert.Contains("Mode: quick", result.Message);
            Assert.Contains("PDF page count: 1", result.Message);
            Assert.Contains("[PDF page 1]", result.Message);
            Assert.Contains("JARVIS PDF extraction test", result.Message);
            Assert.Contains("cite extracted claims", result.Message);
            Assert.Contains("layout-aware text extractor", result.Message);
            Assert.Contains("Layout cues:", result.Message);
            Assert.Contains("Page layout:", result.Message);
            Assert.Contains("embedded raster image objects:", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzePaper_ReturnsSafeFailureForMalformedPdf()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "broken.pdf"), "not a valid PDF");
            var result = await new OfficeWorkspaceTool(root).ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "analyze_paper",
                ["path"] = "broken.pdf"
            });

            Assert.False(result.Success);
            Assert.Contains("could not be analysed safely", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzePaper_RejectsInvalidModeAndPageRanges()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            WriteMinimalPdf(Path.Combine(root, "paper.pdf"));
            var tool = new OfficeWorkspaceTool(root);

            var invalidMode = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "analyze_paper",
                ["path"] = "paper.pdf",
                ["mode"] = "instant"
            });
            Assert.False(invalidMode.Success);
            Assert.Contains("quick or deep", invalidMode.Message);

            var invalidRange = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "analyze_paper",
                ["path"] = "paper.pdf",
                ["start_page"] = "8",
                ["end_page"] = "3"
            });
            Assert.False(invalidRange.Success);
            Assert.Contains("greater than or equal", invalidRange.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task AnalyzePaper_RejectsPageRangesLargerThanModeBudget()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            WriteMinimalPdf(Path.Combine(root, "paper.pdf"));
            var result = await new OfficeWorkspaceTool(root).ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "analyze_paper",
                ["path"] = "paper.pdf",
                ["mode"] = "quick",
                ["start_page"] = "1",
                ["end_page"] = "31"
            });

            Assert.False(result.Success);
            Assert.Contains("limited to 30 pages", result.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateWorkbook_CreatesNewWorkbook()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var tool = new OfficeWorkspaceTool(root);
            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "create_workbook",
                ["path"] = "generated.xlsx",
                ["sheet"] = "Data",
                ["data"] = "[[\"Load\",\"Displacement\"],[100,2.5],[150,3.1]]"
            });

            Assert.True(result.Success, result.Message);
            Assert.True(File.Exists(Path.Combine(root, "generated.xlsx")));

            using var workbook = new XLWorkbook(Path.Combine(root, "generated.xlsx"));
            Assert.Equal("Load", workbook.Worksheet("Data").Cell("A1").GetString());
            Assert.Equal(150d, workbook.Worksheet("Data").Cell("A3").GetDouble());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateWorkbook_DoesNotOverwriteExistingFile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "existing.xlsx");
            File.WriteAllText(path, "keep this file");

            var tool = new OfficeWorkspaceTool(root);
            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "create_workbook",
                ["path"] = "existing.xlsx",
                ["data"] = "[[\"New\"]]"
            });

            Assert.False(result.Success);
            Assert.Equal("keep this file", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateDocument_CreatesReadableWordDocument()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var tool = new OfficeWorkspaceTool(root);
            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "create_document",
                ["path"] = "reports/summary.docx",
                ["title"] = "Structural Analysis Summary",
                ["content"] = "[\"Introduction\",\"The model was analysed under wind and seismic loading.\",\"The results were reviewed against the design criteria.\"]"
            });

            Assert.False(result.Success);
            Assert.Contains("folder must already exist", result.Message);
            Assert.False(File.Exists(Path.Combine(root, "reports", "summary.docx")));

            Directory.CreateDirectory(Path.Combine(root, "reports"));
            result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "create_document",
                ["path"] = "reports/summary.docx",
                ["title"] = "Structural Analysis Summary",
                ["content"] = "[\"Introduction\",\"The model was analysed under wind and seismic loading.\",\"The results were reviewed against the design criteria.\"]"
            });

            Assert.True(result.Success, result.Message);
            using var document = WordprocessingDocument.Open(Path.Combine(root, "reports", "summary.docx"), false);
            var body = document.MainDocumentPart?.Document?.Body;
            Assert.NotNull(body);
            var text = string.Join(" ", body!.Descendants<Text>().Select(x => x.Text));
            Assert.Contains("Structural Analysis Summary", text);
            Assert.Contains("Introduction", text);
            Assert.Contains("wind and seismic loading", text);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task CreateDocument_DoesNotOverwriteExistingFile()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var path = Path.Combine(root, "existing.docx");
            await File.WriteAllTextAsync(path, "keep this file");
            var result = await new OfficeWorkspaceTool(root).ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "create_document",
                ["path"] = "existing.docx",
                ["content"] = "[\"replacement\"]"
            });

            Assert.False(result.Success);
            Assert.Equal("keep this file", await File.ReadAllTextAsync(path));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task InspectDocument_BlocksPathOutsideOfficeWorkspace()
    {
        var root = CreateTemporaryDirectory();
        try
        {
            var outside = Path.Combine(Directory.GetParent(root)!.FullName, "outside.xlsx");
            using (var workbook = new XLWorkbook())
            {
                workbook.Worksheets.Add("Sheet1").Cell("A1").Value = "secret";
                workbook.SaveAs(outside);
            }

            var tool = new OfficeWorkspaceTool(root);
            var result = await tool.ExecuteAsync(new Dictionary<string, string>
            {
                ["action"] = "inspect_document",
                ["path"] = ".." + Path.DirectorySeparatorChar + "outside.xlsx"
            });

            Assert.False(result.Success);
            File.Delete(outside);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static void WriteMinimalPdf(string path)
    {
        const string pageContent = "BT /F1 18 Tf 40 90 Td (JARVIS PDF extraction test) Tj ET";
        var objects = new[]
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [3 0 R] /Count 1 >>",
            "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 300 144] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>",
            $"<< /Length {Encoding.ASCII.GetByteCount(pageContent) + 1} >>\nstream\n{pageContent}\nendstream"
        };

        var pdf = new StringBuilder("%PDF-1.4\n");
        var offsets = new List<int>();
        for (var index = 0; index < objects.Length; index++)
        {
            offsets.Add(Encoding.ASCII.GetByteCount(pdf.ToString()));
            pdf.Append(index + 1).Append(" 0 obj\n")
                .Append(objects[index]).Append("\nendobj\n");
        }

        var xrefOffset = Encoding.ASCII.GetByteCount(pdf.ToString());
        pdf.Append("xref\n0 6\n0000000000 65535 f \n");
        foreach (var offset in offsets)
            pdf.Append(offset.ToString("D10")).Append(" 00000 n \n");
        pdf.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n")
            .Append(xrefOffset).Append("\n%%EOF");

        File.WriteAllText(path, pdf.ToString(), Encoding.ASCII);
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "JarvisOfficeTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
