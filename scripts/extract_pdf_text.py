"""Extract text from PDFs into a private JARVIS library folder (never commit the output).

Usage: python -I extract_pdf_text.py <pylib_dir> <source_dir> <output_dir>
Writes one .txt per PDF with "=== PAGE n ===" markers. Scanned PDFs without a text
layer produce empty pages; those need OCR before JARVIS can search them.
"""
import sys
from pathlib import Path


def main() -> None:
    pylib, source, output = Path(sys.argv[1]), Path(sys.argv[2]), Path(sys.argv[3])
    sys.path.insert(0, str(pylib))
    from pypdf import PdfReader  # imported after the private library path is added

    output.mkdir(parents=True, exist_ok=True)
    for pdf in sorted(source.glob("*.pdf")):
        target = output / (pdf.stem + ".txt")
        if target.exists() and target.stat().st_mtime >= pdf.stat().st_mtime:
            print(f"up to date: {pdf.name}")
            continue
        try:
            reader = PdfReader(str(pdf))
            parts, characters = [], 0
            for number, page in enumerate(reader.pages, start=1):
                text = page.extract_text() or ""
                characters += len(text.strip())
                parts.append(f"\n=== PAGE {number} ===\n{text}")
            target.write_text("".join(parts), encoding="utf-8")
            pages = max(1, len(reader.pages))
            note = "  (little text: probably scanned, needs OCR)" if characters / pages < 200 else ""
            print(f"{pdf.name}: {len(reader.pages)} pages{note}")
        except Exception as error:  # report and continue with the next file
            print(f"FAILED {pdf.name}: {error}")


if __name__ == "__main__":
    main()
