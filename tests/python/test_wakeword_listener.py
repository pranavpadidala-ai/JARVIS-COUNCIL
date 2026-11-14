from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "python"))

from jarvis_runtime.listen_wake_word import validate_model_directory  # noqa: E402


class WakeWordListenerTests(unittest.TestCase):
    def test_model_validation_requires_all_nonempty_assets(self):
        names = (
            "encoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
            "decoder-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
            "joiner-epoch-12-avg-2-chunk-16-left-64.int8.onnx",
            "tokens.txt",
            "keywords.txt",
        )
        with tempfile.TemporaryDirectory() as directory:
            model = Path(directory)
            for name in names:
                (model / name).write_bytes(b"x")
            self.assertEqual(5, len(validate_model_directory(model)))

    def test_model_validation_reports_missing_assets(self):
        with tempfile.TemporaryDirectory() as directory:
            with self.assertRaises(FileNotFoundError):
                validate_model_directory(Path(directory))

    def test_import_does_not_require_microphone_packages(self):
        # sherpa_onnx, sounddevice and numpy are imported only inside listen().
        self.assertTrue(callable(validate_model_directory))


if __name__ == "__main__":
    unittest.main()
