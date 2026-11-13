---
name: engineering-document-reader
description: "Read structural and mechanical engineering PDFs, drawings, equations, tables, plots, finite-element reports and test documentation with page-cited extraction and uncertainty reporting."
triggers: engineering document, engineering PDF, structural drawing, technical paper, read this drawing, dimensions from PDF, equation from paper, finite element report, Abaqus report, material properties, load table, specimen dimensions, test setup
---
# Engineering document reader

Use `office_workspace` action=analyze_paper for PDFs. Prefer deep mode for code checks, modelling details and numerical extraction; use a narrow page range when a particular drawing, table or result is important.

## Read systematically
- Identify document type, revision/date, units and page numbering before interpreting values.
- Extract geometry, section dimensions, material grades/temper, loads, supports, boundary conditions, test arrangement, FE element types, mesh, imperfections, contact and solver settings.
- For equations, preserve symbols, subscripts, signs and units; define every variable only when the source does.
- For plots, report axes, units, series, peaks, trends and approximate values only when legible. Do not confuse plotted points with table values.
- Cross-check dimensions and equations against surrounding text, captions and tables. Cite exact PDF page numbers.
- Separate direct source statements, calculations you perform, and engineering interpretation.
- Mark uncertain OCR, low-resolution labels, ambiguous decimals and missing information. Never silently fill a gap with a plausible value.
- Use GLM-OCR for text and table recognition on rendered PDF pages and embedded raster images. Use the separate Qwen3-VL model for visual interpretation of engineering figures, diagrams, finite-element meshes, plots and boundary conditions, as well as desktop screenshots and GUI grounding. When Gemini cloud vision is enabled, use its full-PDF review as a visual supplement; if full-PDF review is unavailable or fails, fall back to bounded rendered-page image review. With local vision, render up to four selected PDF pages (including scanned pages and vector graphics); use narrow page ranges when exact visual details are important.
- For DXF, INP, CSV or plain-text engineering files, use workspace file inspection and preserve the original syntax; do not treat a PDF-only reader as a CAD parser.
- Desktop screenshots and engineering-figure interpretation use the Qwen3-VL 2B model by default; document text/table OCR uses GLM-OCR. Both local models are configurable with `JARVIS_VISION_MODEL` and `JARVIS_DOCUMENT_VISION_MODEL`.
