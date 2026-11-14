#!/usr/bin/env python3
"""One-shot local Sherpa-ONNX wake-word listener for JARVIS.

The process owns microphone capture. It emits exactly one JSON line after a
keyword is detected, then releases the microphone and exits. No audio is saved
or uploaded.
"""
from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Any


def validate_model_directory(directory: Path) -> list[Path]:
    names = (
        "encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
        "decoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
        "joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
        "tokens.txt",
        "keywords.txt",
    )
    paths = [directory / name for name in names]
    missing = [path.name for path in paths if not path.is_file() or path.stat().st_size == 0]
    if missing:
        raise FileNotFoundError("Missing or empty wake-word assets: " + ", ".join(missing))
    return paths


def listen(model_directory: Path) -> str:
    assets = validate_model_directory(model_directory)

    # Lazy imports keep the source inspectable and its unit tests lightweight.
    import numpy as np
    import sherpa_onnx
    import sounddevice as sd

    spotter = sherpa_onnx.KeywordSpotter(
        tokens=str(assets[3]),
        encoder=str(assets[0]),
        decoder=str(assets[1]),
        joiner=str(assets[2]),
        num_threads=1,
        sample_rate=16_000,
        feature_dim=80,
        max_active_paths=4,
        keywords_file=str(assets[4]),
        keywords_score=1.5,
        keywords_threshold=0.25,
        provider="cpu",
    )
    stream = spotter.create_stream()

    with sd.InputStream(
        channels=1,
        dtype="float32",
        samplerate=16_000,
        blocksize=1_600,
    ) as microphone:
        while True:
            samples, overflowed = microphone.read(1_600)
            if overflowed:
                # Do not feed non-contiguous audio into the current decoder state.
                spotter.reset_stream(stream)

            stream.accept_waveform(
                16_000,
                np.asarray(samples, dtype=np.float32).reshape(-1),
            )
            while spotter.is_ready(stream):
                spotter.decode_stream(stream)
                result: Any = spotter.get_result(stream)
                keyword = str(result or "").strip()
                if keyword:
                    spotter.reset_stream(stream)
                    return keyword


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--model-dir", required=True)
    args = parser.parse_args()

    try:
        keyword = listen(Path(args.model_dir).expanduser().resolve())
    except KeyboardInterrupt:
        return 130
    except Exception as exc:
        print(
            f"Wake-word listener error: {type(exc).__name__}: {exc}",
            file=sys.stderr,
            flush=True,
        )
        return 2

    sys.stdout.write(
        json.dumps({"event": "wake", "keyword": keyword}, ensure_ascii=False) + "\n"
    )
    sys.stdout.flush()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
