using System.Text;
using System.Text.Json;
using ClosedXML.Excel;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using A = DocumentFormat.OpenXml.Drawing;
using Council.Execution.Tools;

namespace Council.App.Office;

/// <summary>
/// Initial Office skill: safe inventory, document inspection, and creation of new Excel workbooks.
/// Existing Office files are never overwritten.
/// </summary>
public sealed class OfficeWorkspaceTool : ITool
{
    private const long MaxDocumentBytes = 50L * 1024 * 1024;
    private readonly string _root;

    public OfficeWorkspaceTool(string? workspaceRoot = null)
    {
        var configured = workspaceRoot ??
            Environment.GetEnvironmentVariable("JARVIS_OFFICE_WORKSPACE");

        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JARVIS Office")
            : configured);

        Directory.CreateDirectory(_root);
    }

    public string Name => "office_workspace";

    public string Description =>
        "Microsoft Office file tool. Actions: list_documents; inspect_document (path for .xlsx/.docx/.pptx); create_workbook (path, data JSON array-of-arrays, optional sheet); create_document (new .docx path, content JSON array of paragraphs, optional title). All paths are relative to the dedicated Office workspace. Creation never overwrites existing files.";

    public Task<ToolResult> ExecuteAsync(
        Dictionary<string, string> arguments,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!arguments.TryGetValue("action", out var action))
            return Task.FromResult(new ToolResult(false, "Required argument: action."));

        try
        {
            var result = action.Trim().ToLowerInvariant() switch
            {
                "list_documents" => ListDocuments(arguments),
                "inspect_document" => InspectDocument(arguments, cancellationToken),
                "create_workbook" => CreateWorkbook(arguments, cancellationToken),
                "create_document" => CreateDocument(arguments, cancellationToken),
                _ => new ToolResult(false, "Unknown Office action. Use list_documents, inspect_document, create_workbook, or create_document.")
            };
            return Task.FromResult(result);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidDataException or ArgumentException or
                                   JsonException or OpenXmlPackageException)
        {
            return Task.FromResult(new ToolResult(false, "Office operation failed safely: " + ex.Message));
        }
    }

    private ToolResult ListDocuments(Dictionary<string, string> args)
    {
        if (args.Keys.Any(key => !key.Equals("action", StringComparison.OrdinalIgnoreCase)))
            return new ToolResult(false, "list_documents accepts only action.");

        var files = EnumerateSafeFiles()
            .Where(IsSupportedOfficeFile)
            .Take(101)
            .ToArray();

        var limited = files.Length > 100;
        var lines = files.Take(100)
            .Select(path => Path.GetRelativePath(_root, path))
            .ToArray();

        return new ToolResult(true,
            "Office workspace: " + _root + Environment.NewLine +
            (lines.Length == 0 ? "No supported Office documents found." : string.Join(Environment.NewLine, lines)) +
            (limited ? Environment.NewLine + "Listing limited to 100 documents." : string.Empty));
    }

    private ToolResult InspectDocument(
        Dictionary<string, string> args,
        CancellationToken cancellationToken)
    {
        if (!HasOnly(args, "action", "path") || !args.TryGetValue("path", out var relativePath))
            return new ToolResult(false, "inspect_document requires only action and path.");

        var path = ResolveExistingFile(relativePath);
        var info = new FileInfo(path);
        if (info.Length > MaxDocumentBytes)
            return new ToolResult(false, "Document exceeds the 50 MB inspection limit.");

        cancellationToken.ThrowIfCancellationRequested();

        return Path.GetExtension(path).ToLowerInvariant() switch
        {
            ".xlsx" => InspectExcel(path, cancellationToken),
            ".docx" => InspectWord(path, cancellationToken),
            ".pptx" => InspectPowerPoint(path, cancellationToken),
            _ => new ToolResult(false, "Supported formats are .xlsx, .docx, and .pptx.")
        };
    }

    private ToolResult InspectExcel(string path, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook(path);
        var output = new StringBuilder();
        output.AppendLine("Excel workbook: " + Path.GetFileName(path));

        foreach (var sheet in workbook.Worksheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var used = sheet.RangeUsed();
            output.AppendLine();
            output.AppendLine("Sheet: " + sheet.Name);

            if (used is null)
            {
                output.AppendLine("Empty sheet.");
                continue;
            }

            output.AppendLine("Used range: " + used.RangeAddress);
            var lastRow = Math.Min(used.LastRow().RowNumber(), used.FirstRow().RowNumber() + 19);
            var lastColumn = Math.Min(used.LastColumn().ColumnNumber(), used.FirstColumn().ColumnNumber() + 11);

            for (var row = used.FirstRow().RowNumber(); row <= lastRow; row++)
            {
                var values = new List<string>();
                for (var column = used.FirstColumn().ColumnNumber(); column <= lastColumn; column++)
                {
                    var cell = sheet.Cell(row, column);
                    var value = cell.GetFormattedString();
                    if (!string.IsNullOrWhiteSpace(cell.FormulaA1))
                        value += " [formula: " + cell.FormulaA1 + "]";
                    values.Add(value.Replace("\r", " ").Replace("\n", " "));
                }
                output.AppendLine(string.Join(" | ", values));
            }

            if (used.RowCount() > 20 || used.ColumnCount() > 12)
                output.AppendLine("[Preview limited to 20 rows x 12 columns.]");
        }

        return new ToolResult(true, output.ToString().Trim());
    }

    private ToolResult InspectWord(string path, CancellationToken cancellationToken)
    {
        using var document = WordprocessingDocument.Open(path, false);
        var body = document.MainDocumentPart?.Document?.Body;
        if (body is null)
            return new ToolResult(false, "Word document has no readable document body.");

        var paragraphs = body.Descendants<Paragraph>()
            .Select(p => string.Concat(p.Descendants<Text>().Select(t => t.Text)))
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .Take(250)
            .ToArray();

        cancellationToken.ThrowIfCancellationRequested();
        return new ToolResult(true,
            "Word document: " + Path.GetFileName(path) + Environment.NewLine +
            string.Join(Environment.NewLine, paragraphs) +
            (paragraphs.Length == 250 ? Environment.NewLine + "[Preview limited to 250 paragraphs.]" : string.Empty));
    }

    private ToolResult InspectPowerPoint(string path, CancellationToken cancellationToken)
    {
        using var document = PresentationDocument.Open(path, false);
        var presentation = document.PresentationPart?.Presentation;
        var slideIds = presentation?.SlideIdList?.ChildElements;
        if (slideIds is null)
            return new ToolResult(false, "PowerPoint presentation has no readable slides.");

        var output = new StringBuilder("PowerPoint: " + Path.GetFileName(path));
        var slideParts = document.PresentationPart!.SlideParts.ToArray();
        for (var i = 0; i < slideParts.Length && i < 100; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var texts = slideParts[i].Slide.Descendants<A.Text>()
                .Select(t => t.Text)
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .ToArray();
            output.AppendLine();
            output.AppendLine("Slide " + (i + 1) + ": " + string.Join(" | ", texts));
        }
        if (slideParts.Length > 100)
            output.AppendLine("[Preview limited to 100 slides.]");

        return new ToolResult(true, output.ToString().Trim());
    }

    private ToolResult CreateDocument(
        Dictionary<string, string> args,
        CancellationToken cancellationToken)
    {
        if (!HasOnly(args, "action", "path", "content", "title") ||
            !args.TryGetValue("path", out var relativePath) ||
            !args.TryGetValue("content", out var contentJson))
            return new ToolResult(false, "create_document requires action, path, and content (JSON array of paragraph strings); title is optional.");

        var path = ResolveNewFile(relativePath, ".docx");
        using var json = JsonDocument.Parse(contentJson);
        if (json.RootElement.ValueKind != JsonValueKind.Array ||
            json.RootElement.GetArrayLength() is < 1 or > 1000)
            return new ToolResult(false, "content must be an array containing 1 to 1000 paragraph strings.");

        var paragraphs = new List<string>();
        var totalCharacters = 0;
        foreach (var item in json.RootElement.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return new ToolResult(false, "Every content item must be a string.");
            var value = item.GetString() ?? string.Empty;
            totalCharacters += value.Length;
            if (totalCharacters > 100_000)
                return new ToolResult(false, "Document text is limited to 100,000 characters.");
            paragraphs.Add(value);
        }

        var title = args.TryGetValue("title", out var requestedTitle)
            ? requestedTitle.Trim()
            : string.Empty;
        if (title.Length > 300)
            return new ToolResult(false, "Document title is limited to 300 characters.");

        cancellationToken.ThrowIfCancellationRequested();
        using (var document = WordprocessingDocument.Create(path, WordprocessingDocumentType.Document))
        {
            var mainPart = document.AddMainDocumentPart();
            var body = new Body();

            if (!string.IsNullOrWhiteSpace(title))
            {
                body.AppendChild(new Paragraph(
                    new ParagraphProperties(new Justification { Val = JustificationValues.Center }),
                    new Run(
                        new RunProperties(
                            new Bold(),
                            new FontSize { Val = "32" }),
                        new Text(title) { Space = SpaceProcessingModeValues.Preserve })));
            }

            foreach (var paragraphText in paragraphs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var paragraph = new Paragraph();
                if (paragraphText.Length > 0)
                    paragraph.AppendChild(new Run(new Text(paragraphText)
                    {
                        Space = SpaceProcessingModeValues.Preserve
                    }));
                body.AppendChild(paragraph);
            }

            body.AppendChild(new SectionProperties(
                new PageSize { Width = 12240, Height = 15840 },
                new PageMargin
                {
                    Top = 1440,
                    Right = 1440,
                    Bottom = 1440,
                    Left = 1440,
                    Header = 720,
                    Footer = 720,
                    Gutter = 0
                }));
            mainPart.Document = new Document(body);
            mainPart.Document.Save();
        }

        return new ToolResult(true,
            "Created new Word document: " + Path.GetRelativePath(_root, path) +
            " (" + paragraphs.Count + " paragraphs, " + totalCharacters + " text characters). Existing files were not overwritten.");
    }

    private ToolResult CreateWorkbook(
        Dictionary<string, string> args,
        CancellationToken cancellationToken)
    {
        if (!HasOnly(args, "action", "path", "data", "sheet") ||
            !args.TryGetValue("path", out var relativePath) ||
            !args.TryGetValue("data", out var dataJson))
            return new ToolResult(false, "create_workbook requires action, path, and data (JSON array of row arrays); sheet is optional.");

        var path = ResolveNewFile(relativePath, ".xlsx");
        using var json = JsonDocument.Parse(dataJson);
        if (json.RootElement.ValueKind != JsonValueKind.Array ||
            json.RootElement.GetArrayLength() is < 1 or > 2000)
            return new ToolResult(false, "data must be an array containing 1 to 2000 rows.");

        var rows = json.RootElement.EnumerateArray().ToArray();
        if (rows.Any(row => row.ValueKind != JsonValueKind.Array))
            return new ToolResult(false, "Every data row must be an array.");

        var cells = rows.Sum(row => row.GetArrayLength());
        if (cells > 50000 || rows.Any(row => row.GetArrayLength() > 100))
            return new ToolResult(false, "Workbook limit: 50,000 cells and 100 columns.");

        var sheetName = args.TryGetValue("sheet", out var requestedSheet) &&
                        !string.IsNullOrWhiteSpace(requestedSheet)
            ? requestedSheet
            : "Sheet1";

        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var rowIndex = 0; rowIndex < rows.Length; rowIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var columnIndex = 0;
            foreach (var value in rows[rowIndex].EnumerateArray())
            {
                columnIndex++;
                var cell = sheet.Cell(rowIndex + 1, columnIndex);
                switch (value.ValueKind)
                {
                    case JsonValueKind.Number:
                        cell.Value = value.GetDouble();
                        break;
                    case JsonValueKind.True:
                    case JsonValueKind.False:
                        cell.Value = value.GetBoolean();
                        break;
                    case JsonValueKind.String:
                        cell.Value = value.GetString() ?? string.Empty;
                        break;
                    case JsonValueKind.Null:
                        cell.Clear();
                        break;
                    default:
                        cell.Value = value.GetRawText();
                        break;
                }
            }
        }

        sheet.ColumnsUsed().AdjustToContents();
        workbook.SaveAs(path);
        return new ToolResult(true,
            "Created new Excel workbook: " + Path.GetRelativePath(_root, path) +
            " (" + rows.Length + " rows, " + cells + " cells). Existing files were not overwritten.");
    }

    private string ResolveExistingFile(string relativePath)
    {
        var path = ResolvePath(relativePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("Office document not found in the Office workspace.", relativePath);
        if (!IsSupportedOfficeFile(path))
            throw new InvalidDataException("Only .xlsx, .docx, and .pptx files are supported.");
        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            throw new UnauthorizedAccessException("Symbolic links and reparse points are not allowed.");
        return path;
    }

    private string ResolveNewFile(string relativePath, string requiredExtension)
    {
        var path = ResolvePath(relativePath);
        if (!string.Equals(Path.GetExtension(path), requiredExtension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("New workbook path must end in " + requiredExtension);
        if (File.Exists(path) || Directory.Exists(path))
            throw new IOException("The destination already exists. Choose a new filename.");
        var parent = Path.GetDirectoryName(path)!;
        if (!Directory.Exists(parent))
            throw new DirectoryNotFoundException("The destination folder must already exist.");
        return path;
    }

    private string ResolvePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
            throw new ArgumentException("Provide a non-empty relative path inside the Office workspace.");

        var fullPath = Path.GetFullPath(Path.Combine(_root, relativePath));
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("Path is outside the Office workspace.");

        var relative = Path.GetRelativePath(_root, fullPath);
        var current = _root;
        var segments = relative.Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
            StringSplitOptions.RemoveEmptyEntries);

        for (var index = 0; index < segments.Length; index++)
        {
            current = Path.Combine(current, segments[index]);
            if ((Directory.Exists(current) || File.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new UnauthorizedAccessException("Symbolic links and reparse points are not allowed.");
        }

        return fullPath;
    }

    private IEnumerable<string> EnumerateSafeFiles()
    {
        var pending = new Stack<string>();
        pending.Push(_root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            foreach (var entry in Directory.EnumerateFileSystemEntries(directory))
            {
                var attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    continue;

                if ((attributes & FileAttributes.Directory) != 0)
                    pending.Push(entry);
                else
                    yield return entry;
            }
        }
    }

    private static bool IsSupportedOfficeFile(string path) =>
        new[] { ".xlsx", ".docx", ".pptx" }
            .Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    private static bool HasOnly(Dictionary<string, string> args, params string[] allowed) =>
        args.Keys.All(key => allowed.Contains(key, StringComparer.OrdinalIgnoreCase));
}
