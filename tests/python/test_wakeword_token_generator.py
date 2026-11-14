from __future__ import annotations

import contextlib
import importlib.util
import io
import sys
import tempfile
import types
import unittest
from pathlib import Path
from unittest.mock import patch


SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "Generate-WakeWordTokens.py"


class FakeSentencePieceProcessor:
    def __init__(self, model_file: str):
        if not Path(model_file).is_file():
            raise FileNotFoundError(model_file)

    def encode(self, text: str, out_type: type[str]) -> list[str]:
        if text == "HEY JARVIS":
            return ["▁HEY", "▁JARVIS"]
        return ["<unknown-token>"]


class WakeWordTokenGeneratorTests(unittest.TestCase):
    def load_script(self):
        fake_sentencepiece = types.ModuleType("sentencepiece")
        fake_sentencepiece.SentencePieceProcessor = FakeSentencePieceProcessor
        with patch.dict(sys.modules, {"sentencepiece": fake_sentencepiece}):
            spec = importlib.util.spec_from_file_location("wakeword_token_generator", SCRIPT)
            self.assertIsNotNone(spec)
            module = importlib.util.module_from_spec(spec)
            assert spec and spec.loader
            spec.loader.exec_module(module)
        return module

    def invoke(self, phrase: str, tokens: Path, bpe: Path, output: Path) -> int:
        module = self.load_script()
        args = [
            str(SCRIPT),
            "--phrase", phrase,
            "--tokens", str(tokens),
            "--bpe-model", str(bpe),
            "--output", str(output),
        ]
        with patch.object(sys, "argv", args):
            with contextlib.redirect_stdout(io.StringIO()):
                return module.main()

    def test_generates_verified_bpe_tokens_and_keyword_label(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tokens = root / "tokens.txt"
            bpe = root / "bpe.model"
            output = root / "keywords.txt"
            tokens.write_text("▁HEY 1\n▁JARVIS 2\n", encoding="utf-8")
            bpe.write_bytes(b"test-model")

            self.assertEqual(0, self.invoke("hey jarvis", tokens, bpe, output))
            self.assertEqual(
                "▁HEY ▁JARVIS #0.35 @HEY_JARVIS\n",
                output.read_text(encoding="utf-8"),
            )

    def test_rejects_invalid_phrase_format(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tokens, bpe, output = root / "tokens.txt", root / "bpe.model", root / "keywords.txt"
            tokens.write_text("▁HEY 1\n", encoding="utf-8")
            bpe.write_bytes(b"test-model")
            with self.assertRaises(SystemExit):
                self.invoke("Hey Jarvis!", tokens, bpe, output)
            self.assertFalse(output.exists())

    def test_rejects_out_of_vocabulary_phrase(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            tokens, bpe, output = root / "tokens.txt", root / "bpe.model", root / "keywords.txt"
            tokens.write_text("▁HEY 1\n", encoding="utf-8")
            bpe.write_bytes(b"test-model")
            with self.assertRaises(SystemExit):
                self.invoke("HEY COMPUTER", tokens, bpe, output)
            self.assertFalse(output.exists())


if __name__ == "__main__":
    unittest.main()
