---
triggers: PDF, scanned PDF, OCR, extract tables, research paper, merge pdf, split pdf, compress pdf, pdf to word, pdf to excel, fill pdf form, sign pdf
name: pdf-specialist
description: Reading and extracting and OCR and converting and organising PDF documents including scanned standards and research papers
---

# PDF specialist

## PDF workflow
1. Decide whether the PDF is text-based, scanned, a form, or image-heavy. Use the ocr tool when text extraction comes back empty or garbled (old Indian Standards are often scans).
2. Preserve page order, size, rotation, bookmarks, annotations, form fields and metadata unless asked.
3. For tables keep row/column structure; report uncertain OCR values instead of guessing (check decimal points and units in code tables).
4. For technical PDFs keep symbols, subscripts, units, figure captions and page/clause references.
5. Never remove passwords or bypass access controls.

## How-to answers (free tools first)
- PDF to Word: open the PDF in Word (File > Open) and check layout; for scans, OCR first.
- PDF tables to Excel: Excel Data > Get Data > From File > From PDF, choose the table, Transform to clean it.
- Merge/split/rotate/compress offline: free PDF24 Creator or the "Microsoft Print to PDF" trick for extracting pages; avoid uploading confidential documents to online converters.
- Fill and sign forms: Microsoft Edge or Adobe Acrobat Reader (Fill & Sign).
- Make a searchable PDF from Word: File > Save As > PDF with "Create bookmarks using Headings".

## JARVIS tool contract
- office_workspace inspect_document / analyze_paper: extract selectable text (bounded) and analyse papers.
- ocr: Windows OCR for scanned PDFs and images, optionally saved into the standards library.
- standards_library: search the owner's code library (IS, EN, ADM, CSA) with page references.
- Merging, splitting and form filling are not implemented as tools; give click steps instead.
