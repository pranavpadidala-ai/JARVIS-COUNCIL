#!/usr/bin/env python3
"""Generate a sherpa-onnx BPE keyword line from a user-selected English phrase."""

from __future__ import annotations

import argparse
import re
from pathlib import Path

import sentencepiece as spm


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--phrase", required=True)
    parser.add_argument("--tokens", required=True)
    parser.add_argument("--bpe-model", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args()

    phrase = " ".join(args.phrase.strip().upper().split())
    if not re.fullmatch(r"[A-Z0-9]+(?: [A-Z0-9]+){1,3}", phrase):
        raise SystemExit("Wake phrase must contain 2-4 words using English letters and numbers.")

    token_file = Path(args.tokens)
    bpe_file = Path(args.bpe_model)
    output_file = Path(args.output)
    for path in (token_file, bpe_file):
        if not path.is_file():
            raise SystemExit(f"Required model file not found: {path}")

    vocabulary: set[str] = set()
    with token_file.open("r", encoding="utf-8") as source:
        for line in source:
            parts = line.split()
            if len(parts) == 2:
                vocabulary.add(parts[0])

    processor = spm.SentencePieceProcessor(model_file=str(bpe_file))
    pieces = processor.encode(phrase, out_type=str)
    if not pieces:
        raise SystemExit("SentencePiece returned no tokens for the wake phrase.")

    unknown = [piece for piece in pieces if piece not in vocabulary]
    if unknown:
        raise SystemExit(
            "The selected phrase contains tokens absent from this model's vocabulary: "
            + ", ".join(unknown)
        )

    label = re.sub(r"[^A-Z0-9]+", "_", phrase).strip("_")
    # Use a conservative threshold; real-room microphone testing is still needed.
    line = " ".join(pieces) + " #0.35 @" + label
    output_file.parent.mkdir(parents=True, exist_ok=True)
    output_file.write_text(line + "\n", encoding="utf-8")
    print(f"Created wake-word tokens for: {phrase}")
    print(f"Output: {output_file}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
