---
name: pdf-research
description: Extract, inspect, summarize, compare and quality-check PDFs including scanned technical papers.
triggers: pdf, scanned pdf, extract pdf, summarize paper, read pdf, OCR, tables in pdf, PDF pages
---
# PDF research specialist

- Determine whether the PDF is text-based, scanned, or mixed. Inspect page count, orientation, columns, tables, figures and appendices.
- Preserve page numbers and cite the exact page for extracted claims.
- Treat OCR as fallible, especially for equations, subscripts, symbols, tables and decimal points. Verify critical values visually.
- Do not reconstruct missing equations or tables by guessing.
- Summarize methods, inputs, results, limitations and conclusions separately.
- For large files, process relevant sections in logical order and maintain a source-to-claim trail.
- Use office_workspace action inspect_document for selectable PDF text. The current parser previews at most 100 pages and 50,000 characters. Scanned/image-only PDFs are not OCR'd; ask for a text-based PDF or state that OCR is unavailable. Never claim visual verification or OCR has occurred.
