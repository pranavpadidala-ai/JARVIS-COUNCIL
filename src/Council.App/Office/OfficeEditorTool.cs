using System.Text.Json;
using System.Text.RegularExpressions;
using ClosedXML.Excel;
using Council.Execution.Tools;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;

namespace Council.App.Office;

/// <summary>
/// Builds and edits Office files the way an expert would, inside the Office workspace
/// (Documents\JARVIS Office). Every save goes to a new file; originals are never changed.
///   excel_write: live formulas, number formats, styling, widths, freeze panes, AutoFilter,
///                drop-down validation and highlight rules (copy of an existing .xlsx, or a new one).
///   word_build:  a new report with real Title/Heading/Caption styles, lists, tables, page breaks and a TOC.
///   word_edit:   a copy of an existing .docx with find-and-replace and appended styled blocks.
/// </summary>
public sealed class OfficeEditorTool : ITool
{
    private const int MaxCells = 20_000;
    private const int MaxBlocks = 2_000;
    private readonly string _root;

    public OfficeEditorTool(string? workspaceRoot = null)
    {
        var configured = workspaceRoot ?? Environment.GetEnvironmentVariable("JARVIS_OFFICE_WORKSPACE");
        _root = Path.GetFullPath(string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "JARVIS Office")
            : configured);
        Directory.CreateDirectory(_root);
    }

    public string Name => "office_editor";

    public string Description =>
        "Expert Office editing in the Office workspace (paths relative to Documents\\JARVIS Office); always saves to a NEW output file. Actions:\n" +
        "excel_write: output (new .xlsx), optional path (existing .xlsx to copy), sheet, cells (JSON object {\"A1\": value or \"=FORMULA\"}), " +
        "formats (JSON {\"A1:F1\": {\"bold\":true,\"fill\":\"#DDEBF7\",\"font_color\":\"#C00000\",\"number_format\":\"0.00\",\"border\":true,\"wrap\":true,\"align\":\"center\"}}), " +
        "column_widths (JSON {\"A\": 18} or \"auto\"), freeze (e.g. \"A2\"), autofilter (e.g. \"A1:F50\"), " +
        "validation (JSON [{\"range\":\"B2:B50\",\"list\":[\"Fe250\",\"Fe410\"]}]), highlight (JSON [{\"range\":\"F2:F50\",\"when\":\"greater_than\",\"value\":1,\"fill\":\"#FFC7CE\"}]; when = greater_than|less_than|equal|between with value2).\n" +
        "word_build: output (new .docx), optional title, blocks (JSON array of {\"type\":\"heading\",\"level\":1-3,\"text\"} | {\"type\":\"paragraph\",\"text\"} | " +
        "{\"type\":\"bullets\",\"items\":[...]} | {\"type\":\"numbered\",\"items\":[...]} | {\"type\":\"table\",\"rows\":[[...]],\"caption\":\"Table 1: ...\"} | " +
        "{\"type\":\"caption\",\"text\"} | {\"type\":\"page_break\"} | {\"type\":\"toc\"}). Headings use real Heading styles so the TOC and navigation pane work.\n" +
        "word_edit: path (existing .docx), output (new .docx), optional replace (JSON [{\"find\":\"old\",\"replace\":\"new\"}]) and append (blocks as in word_build).";

    public Task<ToolResult> ExecuteAsync(Dictionary<string, string> arguments, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = new Dictionary<string, string>(arguments, StringComparer.OrdinalIgnoreCase);
        try
        {
            var action = Arg(args, "action").ToLowerInvariant();
            return Task.FromResult(action switch
            {
                "excel_write" => ExcelWrite(args, cancellationToken),
                "word_build" => WordBuild(args, cancellationToken),
                "word_edit" => WordEdit(args, cancellationToken),
                _ => new ToolResult(false, "action must be excel_write, word_build or word_edit.")
            });
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or JsonException
                                       or InvalidDataException or KeyNotFoundException or FormatException or OpenXmlPackageException)
        {
            return Task.FromResult(new ToolResult(false, "Office edit failed: " + ex.Message));
        }
    }

    // ---------------- Excel ----------------

    private ToolResult ExcelWrite(Dictionary<string, string> args, CancellationToken token)
    {
        var output = NewFile(Arg(args, "output"), ".xlsx");
        var source = Arg(args, "path");
        using var workbook = source.Length > 0 ? new XLWorkbook(ExistingFile(source, ".xlsx")) : new XLWorkbook();
        var sheetName = Arg(args, "sheet");
        IXLWorksheet sheet;
        if (sheetName.Length == 0)
            sheet = workbook.Worksheets.Count > 0 ? workbook.Worksheet(1) : workbook.Worksheets.Add("Sheet1");
        else
            sheet = workbook.Worksheets.TryGetWorksheet(sheetName, out var found) ? found : workbook.Worksheets.Add(sheetName);

        var written = 0;
        var formulas = 0;
        if (Arg(args, "cells") is { Length: > 0 } cellsJson)
        {
            using var doc = JsonDocument.Parse(cellsJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Object) throw new ArgumentException("cells must be a JSON object like {\"A1\": 5}.");
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                token.ThrowIfCancellationRequested();
                if (++written > MaxCells) throw new ArgumentException($"At most {MaxCells} cells per call.");
                var cell = sheet.Cell(CellAddress(property.Name));
                var value = property.Value;
                switch (value.ValueKind)
                {
                    case JsonValueKind.Number: cell.Value = value.GetDouble(); break;
                    case JsonValueKind.True or JsonValueKind.False: cell.Value = value.GetBoolean(); break;
                    case JsonValueKind.Null: cell.Clear(XLClearOptions.Contents); break;
                    case JsonValueKind.String:
                        var text = value.GetString() ?? "";
                        if (text.StartsWith('=') && text.Length > 1) { cell.FormulaA1 = text[1..]; formulas++; }
                        else cell.Value = text;
                        break;
                    default: cell.Value = value.GetRawText(); break;
                }
            }
        }

        if (Arg(args, "formats") is { Length: > 0 } formatsJson)
        {
            using var doc = JsonDocument.Parse(formatsJson);
            foreach (var property in doc.RootElement.EnumerateObject())
                ApplyFormat(sheet.Range(RangeAddress(property.Name)), property.Value);
        }

        var widths = Arg(args, "column_widths");
        if (widths.Equals("auto", StringComparison.OrdinalIgnoreCase)) sheet.ColumnsUsed().AdjustToContents();
        else if (widths.Length > 0)
        {
            using var doc = JsonDocument.Parse(widths);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (!Regex.IsMatch(property.Name, "^[A-Za-z]{1,3}$")) throw new ArgumentException("column_widths keys must be column letters.");
                sheet.Column(property.Name.ToUpperInvariant()).Width = Math.Clamp(property.Value.GetDouble(), 1, 200);
            }
        }

        if (Arg(args, "freeze") is { Length: > 0 } freeze)
        {
            var at = sheet.Cell(CellAddress(freeze));
            sheet.SheetView.Freeze(at.Address.RowNumber - 1, at.Address.ColumnNumber - 1);
        }

        if (Arg(args, "autofilter") is { Length: > 0 } filter)
        {
            sheet.AutoFilter.Clear();
            sheet.Range(RangeAddress(filter)).SetAutoFilter();
        }

        var validations = 0;
        if (Arg(args, "validation") is { Length: > 0 } validationJson)
        {
            using var doc = JsonDocument.Parse(validationJson);
            foreach (var rule in doc.RootElement.EnumerateArray())
            {
                var items = rule.GetProperty("list").EnumerateArray().Select(i => i.ToString().Replace("\"", "")).ToList();
                if (items.Count == 0 || string.Join(",", items).Length > 250) throw new ArgumentException("A drop-down list needs 1-250 characters of items.");
                var range = sheet.Range(RangeAddress(rule.GetProperty("range").GetString() ?? ""));
                range.CreateDataValidation().List("\"" + string.Join(",", items) + "\"", true);
                validations++;
            }
        }

        var highlights = 0;
        if (Arg(args, "highlight") is { Length: > 0 } highlightJson)
        {
            using var doc = JsonDocument.Parse(highlightJson);
            foreach (var rule in doc.RootElement.EnumerateArray())
            {
                var range = sheet.Range(RangeAddress(rule.GetProperty("range").GetString() ?? ""));
                var value = rule.GetProperty("value").GetDouble();
                var conditional = range.AddConditionalFormat();
                var style = (rule.TryGetProperty("when", out var w) ? w.GetString() : "greater_than") switch
                {
                    "less_than" => conditional.WhenLessThan(value),
                    "equal" => conditional.WhenEquals(value),
                    "between" => conditional.WhenBetween(value, rule.GetProperty("value2").GetDouble()),
                    "greater_than" or null => conditional.WhenGreaterThan(value),
                    var other => throw new ArgumentException("Unknown highlight rule " + other)
                };
                style.Fill.SetBackgroundColor(Color(rule.TryGetProperty("fill", out var f) ? f.GetString() : "#FFC7CE"));
                if (rule.TryGetProperty("font_color", out var fc)) style.Font.SetFontColor(Color(fc.GetString()));
                highlights++;
            }
        }

        workbook.SaveAs(output);
        return new ToolResult(true,
            $"Saved {Rel(output)} (sheet '{sheet.Name}': {written} cell(s) written, {formulas} live formula(s), " +
            $"{validations} drop-down rule(s), {highlights} highlight rule(s)). The original was not changed. " +
            "Excel calculates the formulas when the file is opened.");
    }

    private static void ApplyFormat(IXLRange range, JsonElement f)
    {
        foreach (var p in f.EnumerateObject())
        {
            switch (p.Name.ToLowerInvariant())
            {
                case "bold": range.Style.Font.Bold = p.Value.GetBoolean(); break;
                case "italic": range.Style.Font.Italic = p.Value.GetBoolean(); break;
                case "font_size": range.Style.Font.FontSize = Math.Clamp(p.Value.GetDouble(), 6, 72); break;
                case "font_color": range.Style.Font.FontColor = Color(p.Value.GetString()); break;
                case "fill": range.Style.Fill.BackgroundColor = Color(p.Value.GetString()); break;
                case "number_format": range.Style.NumberFormat.Format = p.Value.GetString() ?? "General"; break;
                case "wrap": range.Style.Alignment.WrapText = p.Value.GetBoolean(); break;
                case "align":
                    range.Style.Alignment.Horizontal = (p.Value.GetString() ?? "").ToLowerInvariant() switch
                    {
                        "center" or "centre" => XLAlignmentHorizontalValues.Center,
                        "right" => XLAlignmentHorizontalValues.Right,
                        "left" => XLAlignmentHorizontalValues.Left,
                        _ => XLAlignmentHorizontalValues.General
                    };
                    break;
                case "border":
                    if (p.Value.GetBoolean())
                    {
                        range.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        range.Style.Border.InsideBorder = XLBorderStyleValues.Thin;
                    }
                    break;
                default: throw new ArgumentException("Unknown format option " + p.Name);
            }
        }
    }

    private static XLColor Color(string? hex)
    {
        hex = (hex ?? "").Trim();
        if (!Regex.IsMatch(hex, "^#?[0-9A-Fa-f]{6}$")) throw new ArgumentException("Colours must be hex like #DDEBF7.");
        return XLColor.FromHtml(hex.StartsWith('#') ? hex : "#" + hex);
    }

    private static string CellAddress(string a) =>
        Regex.IsMatch(a.Trim(), "^[A-Za-z]{1,3}[1-9][0-9]{0,6}$") ? a.Trim().ToUpperInvariant() : throw new ArgumentException($"'{a}' is not a cell address like B7.");

    private static string RangeAddress(string a)
    {
        a = a.Trim().ToUpperInvariant();
        return Regex.IsMatch(a, "^[A-Z]{1,3}[1-9][0-9]{0,6}(:[A-Z]{1,3}[1-9][0-9]{0,6})?$") ? a : throw new ArgumentException($"'{a}' is not a range like A1:F20.");
    }

    // ---------------- Word ----------------

    private ToolResult WordBuild(Dictionary<string, string> args, CancellationToken token)
    {
        var output = NewFile(Arg(args, "output"), ".docx");
        var blocks = ParseBlocks(Arg(args, "blocks"));
        using (var document = WordprocessingDocument.Create(output, WordprocessingDocumentType.Document))
        {
            var main = document.AddMainDocumentPart();
            var body = new Body();
            main.Document = new Document(body);
            EnsureStyles(main);
            if (Arg(args, "title") is { Length: > 0 } title) body.AppendChild(StyledParagraph("Title", title));
            var hasToc = AppendBlocks(main, body, blocks, token);
            body.AppendChild(new SectionProperties(
                new PageSize { Width = 11906, Height = 16838 }, // A4
                new PageMargin { Top = 1440, Right = 1440, Bottom = 1440, Left = 1440, Header = 720, Footer = 720, Gutter = 0 }));
            if (hasToc) AskWordToUpdateFields(main);
            main.Document.Save();
        }
        return new ToolResult(true, $"Created {Rel(output)} with {blocks.Count} block(s) using real Word styles." +
                                    " If it has a table of contents, Word asks to update fields when the file opens: click Yes.");
    }

    private ToolResult WordEdit(Dictionary<string, string> args, CancellationToken token)
    {
        var source = ExistingFile(Arg(args, "path"), ".docx");
        var output = NewFile(Arg(args, "output"), ".docx");
        var replacements = new List<(string Find, string Replace)>();
        if (Arg(args, "replace") is { Length: > 0 } replaceJson)
        {
            using var doc = JsonDocument.Parse(replaceJson);
            foreach (var r in doc.RootElement.EnumerateArray())
            {
                var find = r.GetProperty("find").GetString() ?? "";
                if (find.Length == 0) throw new ArgumentException("replace items need a non-empty find.");
                replacements.Add((find, r.GetProperty("replace").GetString() ?? ""));
            }
        }
        var blocks = Arg(args, "append") is { Length: > 0 } a ? ParseBlocks(a) : new List<JsonElement>();
        if (replacements.Count == 0 && blocks.Count == 0) throw new ArgumentException("Give replace and/or append.");

        File.Copy(source, output);
        var counts = new int[replacements.Count];
        using (var document = WordprocessingDocument.Open(output, true))
        {
            var main = document.MainDocumentPart ?? throw new InvalidDataException("The document has no body.");
            var body = main.Document.Body ?? throw new InvalidDataException("The document has no body.");
            var parts = new List<OpenXmlPartRootElement> { main.Document };
            parts.AddRange(main.HeaderParts.Select(h => (OpenXmlPartRootElement)h.Header));
            parts.AddRange(main.FooterParts.Select(f => (OpenXmlPartRootElement)f.Footer));
            for (var i = 0; i < replacements.Count; i++)
                foreach (var root in parts)
                    foreach (var paragraph in root.Descendants<Paragraph>())
                        counts[i] += ReplaceInParagraph(paragraph, replacements[i].Find, replacements[i].Replace);

            if (blocks.Count > 0)
            {
                EnsureStyles(main);
                var section = body.Elements<SectionProperties>().LastOrDefault();
                var holder = new Body();
                if (AppendBlocks(main, holder, blocks, token)) AskWordToUpdateFields(main);
                foreach (var element in holder.ChildElements.ToList())
                {
                    element.Remove();
                    if (section is not null) body.InsertBefore(element, section); else body.AppendChild(element);
                }
            }
            main.Document.Save();
        }
        var summary = replacements.Select((r, i) => $"'{r.Find}' -> '{r.Replace}': {counts[i]}");
        return new ToolResult(true, $"Saved {Rel(output)} (original unchanged). " +
                                    (replacements.Count > 0 ? "Replacements: " + string.Join("; ", summary) + ". " : "") +
                                    (blocks.Count > 0 ? $"Appended {blocks.Count} block(s)." : ""));
    }

    /// <summary>
    /// Replaces text inside one run when possible (keeps formatting). When the text spans several runs,
    /// the paragraph's text is rebuilt in its first run, keeping that run's formatting.
    /// </summary>
    public static int ReplaceInParagraph(Paragraph paragraph, string find, string replace)
    {
        var full = string.Concat(paragraph.Descendants<Text>().Select(t => t.Text));
        var count = Regex.Matches(full, Regex.Escape(find)).Count;
        if (count == 0) return 0;

        var texts = paragraph.Descendants<Text>().ToList();
        if (texts.Sum(t => Regex.Matches(t.Text, Regex.Escape(find)).Count) == count)
        {
            foreach (var t in texts.Where(t => t.Text.Contains(find, StringComparison.Ordinal)))
            {
                t.Text = t.Text.Replace(find, replace, StringComparison.Ordinal);
                t.Space = SpaceProcessingModeValues.Preserve;
            }
            return count;
        }

        texts[0].Text = full.Replace(find, replace, StringComparison.Ordinal);
        texts[0].Space = SpaceProcessingModeValues.Preserve;
        foreach (var t in texts.Skip(1)) t.Text = "";
        return count;
    }

    private static List<JsonElement> ParseBlocks(string json)
    {
        if (json.Length == 0) throw new ArgumentException("blocks is required (JSON array).");
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new ArgumentException("blocks must be a JSON array.");
        var list = doc.RootElement.EnumerateArray().Select(e => e.Clone()).ToList();
        if (list.Count is 0 or > MaxBlocks) throw new ArgumentException($"Give 1 to {MaxBlocks} blocks.");
        return list;
    }

    private static bool AppendBlocks(MainDocumentPart main, OpenXmlElement body, List<JsonElement> blocks, CancellationToken token)
    {
        var hasToc = false;
        foreach (var block in blocks)
        {
            token.ThrowIfCancellationRequested();
            string Text(string name = "text") => block.TryGetProperty(name, out var v) ? v.GetString() ?? "" : "";
            var type = block.TryGetProperty("type", out var t) ? (t.GetString() ?? "").ToLowerInvariant() : "paragraph";
            switch (type)
            {
                case "heading":
                    var level = block.TryGetProperty("level", out var l) ? Math.Clamp(l.GetInt32(), 1, 3) : 1;
                    body.AppendChild(StyledParagraph("Heading" + level, Text()));
                    break;
                case "paragraph":
                    body.AppendChild(StyledParagraph("Normal", Text()));
                    break;
                case "caption":
                    body.AppendChild(StyledParagraph("Caption", Text()));
                    break;
                case "bullets":
                case "numbered":
                    var n = 0;
                    foreach (var item in block.GetProperty("items").EnumerateArray())
                    {
                        n++;
                        var marker = type == "bullets" ? "•\t" : n + ".\t";
                        var p = StyledParagraph("ListParagraph", marker + item.GetString());
                        p.ParagraphProperties!.Append(new Indentation { Left = "720", Hanging = "360" });
                        body.AppendChild(p);
                    }
                    break;
                case "table":
                    if (block.TryGetProperty("caption", out var cap)) body.AppendChild(StyledParagraph("Caption", cap.GetString() ?? ""));
                    body.AppendChild(BuildTable(block.GetProperty("rows")));
                    body.AppendChild(new Paragraph());
                    break;
                case "page_break":
                    body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                    break;
                case "toc":
                    body.AppendChild(StyledParagraph("TOCHeading", "Contents"));
                    body.AppendChild(TocField());
                    body.AppendChild(new Paragraph(new Run(new Break { Type = BreakValues.Page })));
                    hasToc = true;
                    break;
                default:
                    throw new ArgumentException("Unknown block type " + type);
            }
        }
        return hasToc;
    }

    private static Paragraph StyledParagraph(string styleId, string text) =>
        new(new ParagraphProperties(new ParagraphStyleId { Val = styleId }),
            new Run(new Text(text) { Space = SpaceProcessingModeValues.Preserve }));

    private static Table BuildTable(JsonElement rows)
    {
        var table = new Table(new TableProperties(
            new TableStyle { Val = "TableGrid" },
            new TableWidth { Width = "5000", Type = TableWidthUnitValues.Pct },
            new TableBorders(
                new TopBorder { Val = BorderValues.Single, Size = 4 }, new LeftBorder { Val = BorderValues.Single, Size = 4 },
                new BottomBorder { Val = BorderValues.Single, Size = 4 }, new RightBorder { Val = BorderValues.Single, Size = 4 },
                new InsideHorizontalBorder { Val = BorderValues.Single, Size = 4 }, new InsideVerticalBorder { Val = BorderValues.Single, Size = 4 })));
        var columns = Math.Max(1, rows.EnumerateArray().Select(r => r.GetArrayLength()).DefaultIfEmpty(1).Max());
        var grid = new TableGrid();
        for (var c = 0; c < columns; c++) grid.Append(new GridColumn { Width = (9000 / columns).ToString() });
        table.Append(grid);
        var first = true;
        foreach (var row in rows.EnumerateArray())
        {
            var tr = new TableRow();
            if (first) tr.Append(new TableRowProperties(new TableHeader())); // repeats on each page
            foreach (var cell in row.EnumerateArray())
            {
                var run = new Run(new Text(cell.ValueKind == JsonValueKind.String ? cell.GetString() ?? "" : cell.GetRawText()) { Space = SpaceProcessingModeValues.Preserve });
                if (first) run.PrependChild(new RunProperties(new Bold()));
                tr.Append(new TableCell(new Paragraph(new ParagraphProperties(new SpacingBetweenLines { After = "0" }), run)));
            }
            table.Append(tr);
            first = false;
        }
        return table;
    }

    private static Paragraph TocField() =>
        new(
            new Run(new FieldChar { FieldCharType = FieldCharValues.Begin, Dirty = true }),
            new Run(new FieldCode(" TOC \\o \"1-3\" \\h \\z \\u ") { Space = SpaceProcessingModeValues.Preserve }),
            new Run(new FieldChar { FieldCharType = FieldCharValues.Separate }),
            new Run(new Text("Right-click and choose Update Field to build the table of contents.")),
            new Run(new FieldChar { FieldCharType = FieldCharValues.End }));

    private static void AskWordToUpdateFields(MainDocumentPart main)
    {
        var settingsPart = main.DocumentSettingsPart ?? main.AddNewPart<DocumentSettingsPart>();
        settingsPart.Settings ??= new Settings();
        if (!settingsPart.Settings.Elements<UpdateFieldsOnOpen>().Any())
            settingsPart.Settings.Append(new UpdateFieldsOnOpen { Val = true });
        settingsPart.Settings.Save();
    }

    /// <summary>Adds the styles JARVIS uses (only those missing), so headings feed the TOC and navigation pane.</summary>
    private static void EnsureStyles(MainDocumentPart main)
    {
        var part = main.StyleDefinitionsPart ?? main.AddNewPart<StyleDefinitionsPart>();
        part.Styles ??= new Styles();
        var styles = part.Styles;
        bool Has(string id) => styles.Elements<Style>().Any(s => s.StyleId?.Value == id);

        if (!Has("Normal"))
            styles.Append(new Style(
                new StyleName { Val = "Normal" }, new PrimaryStyle(),
                new StyleParagraphProperties(new SpacingBetweenLines { After = "120", Line = "360", LineRule = LineSpacingRuleValues.Auto }),
                new StyleRunProperties(new RunFonts { Ascii = "Calibri", HighAnsi = "Calibri", ComplexScript = "Calibri" }, new FontSize { Val = "24" }))
            { Type = StyleValues.Paragraph, StyleId = "Normal", Default = true });

        void Add(string id, string name, int sizeHalfPoints, bool bold, int? outline, string color = "1F3864", string spacingBefore = "240")
        {
            if (Has(id)) return;
            var paragraph = new StyleParagraphProperties(new KeepNext(), new SpacingBetweenLines { Before = spacingBefore, After = "120" });
            if (outline is { } o) paragraph.Append(new OutlineLevel { Val = o });
            var run = new StyleRunProperties(new Color { Val = color }, new FontSize { Val = sizeHalfPoints.ToString() });
            if (bold) run.PrependChild(new Bold());
            styles.Append(new Style(new StyleName { Val = name }, new BasedOn { Val = "Normal" }, new NextParagraphStyle { Val = "Normal" },
                new PrimaryStyle(), paragraph, run)
            { Type = StyleValues.Paragraph, StyleId = id });
        }

        Add("Title", "Title", 52, true, null, spacingBefore: "0");
        Add("Heading1", "heading 1", 32, true, 0);
        Add("Heading2", "heading 2", 28, true, 1);
        Add("Heading3", "heading 3", 24, true, 2);
        Add("TOCHeading", "TOC Heading", 32, true, null);
        Add("Caption", "caption", 20, true, null, "44546A", "60");
        Add("ListParagraph", "List Paragraph", 24, false, null, "000000", "0");
        if (!Has("TableGrid"))
            styles.Append(new Style(new StyleName { Val = "Table Grid" }) { Type = StyleValues.Table, StyleId = "TableGrid" });
        part.Styles.Save();
    }

    // ---------------- paths ----------------

    private string ExistingFile(string relative, string extension)
    {
        var path = Resolve(relative);
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"path must be a {extension} file.");
        if (!File.Exists(path)) throw new FileNotFoundException($"{relative} was not found in the Office workspace ({_root}).");
        return path;
    }

    private string NewFile(string relative, string extension)
    {
        if (relative.Length == 0) throw new ArgumentException("output (a new file name) is required.");
        var path = Resolve(relative);
        if (!path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"output must end in {extension}.");
        if (File.Exists(path)) throw new IOException($"{relative} already exists; choose a new name (originals are never overwritten).");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path;
    }

    private string Resolve(string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative))
            throw new ArgumentException("Use a path relative to the Office workspace (Documents\\JARVIS Office).");
        var full = Path.GetFullPath(Path.Combine(_root, relative));
        var prefix = _root.EndsWith(Path.DirectorySeparatorChar) ? _root : _root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException("Path is outside the Office workspace.");
        return full;
    }

    private string Rel(string full) => Path.GetRelativePath(_root, full);

    private static string Arg(Dictionary<string, string> args, string key) => args.TryGetValue(key, out var v) ? v.Trim() : "";
}
