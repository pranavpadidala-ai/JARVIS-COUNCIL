---
triggers: PDF, scanned PDF, OCR, extract tables, PDF document, research paper
name: pdf-specialist
description: Use for reading, extracting, merging, splitting, rotating, OCR, forms, and creating PDF documents.
---

# PDF specialist

## PDF workflow
1. Determine whether the PDF is text-based, scanned, form-based, or image-heavy. Use page rendering/OCR when text extraction is incomplete.
2. Preserve page order, page size, rotation, bookmarks, annotations, form fields, and metadata unless the user asks to change them.
3. For tables, preserve row/column structure and report uncertain OCR values instead of guessing.
4. For technical PDFs, retain equation symbols, subscripts, superscripts, units, figure captions, and page references.
5. When merging or splitting, verify page count and order. When filling forms, map fields explicitly and preserve the original as a separate copy.
6. Never remove passwords or bypass access controls without authorization.
7. Verify output by reopening and checking page count, text, and representative rendered pages.

## Current JARVIS tool contract
Use office_workspace action inspect_document with a relative PDF path to extract selectable text. Extraction is limited to 100 pages or 50,000 characters per inspection. Scanned/image-only PDFs return no selectable text; OCR, visual page rendering, PDF editing, merging, splitting, and form filling are not implemented. Never claim OCR or visual verification has been performed.
