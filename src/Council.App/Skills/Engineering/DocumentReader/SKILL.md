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
- When Gemini cloud vision is enabled, use its full-PDF review as a visual supplement to local PDF text extraction. When unavailable, state that scanned pages/vector graphics may not have been fully read.
- For DXF, INP, CSV or plain-text engineering files, use workspace file inspection and preserve the original syntax; do not treat a PDF-only reader as a CAD parser.
