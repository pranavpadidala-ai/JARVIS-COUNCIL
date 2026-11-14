from __future__ import annotations

import sys
import tempfile
import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / "python"))

from jarvis_runtime.worker import handle  # noqa: E402


class PythonWorkerTests(unittest.TestCase):
    def test_health_reports_current_interpreter(self):
        result = handle({"action": "health"})
        self.assertTrue(result["success"])
        self.assertTrue(result["data"]["python"])
        self.assertTrue(Path(result["data"]["executable"]).exists())

    def test_valid_source_passes_syntax_check_without_execution(self):
        result = handle({
            "action": "check_syntax",
            "source": "def add(a, b):\n    return a + b\n",
        })
        self.assertTrue(result["success"])
        self.assertFalse(result["data"]["executed"])

    def test_invalid_source_returns_location(self):
        result = handle({
            "action": "check_syntax",
            "source": "def broken(:\n    pass\n",
        })
        self.assertFalse(result["success"])
        self.assertEqual(1, result["data"]["line"])
        self.assertIn("syntax error", result["message"].lower())

    def test_inspect_file_returns_ast_outline(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "sample.py").write_text(
                "import json\n\nclass Worker:\n    def run(self):\n        return json.dumps({})\n",
                encoding="utf-8",
            )
            result = handle({"action": "inspect_file", "workspace": directory, "path": "sample.py"})
        self.assertTrue(result["success"])
        self.assertIn("json", [item["module"] for item in result["data"]["imports"]])
        self.assertIn("Worker", [item["name"] for item in result["data"]["classes"]])
        self.assertIn("run", [item["name"] for item in result["data"]["functions"]])

    def test_rejects_path_traversal(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            outside = root.parent / (root.name + "-outside.py")
            outside.write_text("print('not read')\n", encoding="utf-8")
            try:
                result = handle({
                    "action": "inspect_file",
                    "workspace": directory,
                    "path": "../" + outside.name,
                })
                self.assertFalse(result["success"])
                self.assertIn("outside", result["message"].lower())
            finally:
                outside.unlink(missing_ok=True)

    def test_rejects_python_files_above_size_limit(self):
        with tempfile.TemporaryDirectory() as directory:
            root = Path(directory)
            (root / "large.py").write_text("#" + ("x" * 600_000), encoding="utf-8")
            result = handle({"action": "inspect_file", "workspace": directory, "path": "large.py"})
        self.assertFalse(result["success"])
        self.assertIn("512 KB", result["message"])

    def test_unsupported_action_is_rejected(self):
        result = handle({"action": "execute_shell"})
        self.assertFalse(result["success"])
        self.assertIn("Unsupported action", result["message"])


if __name__ == "__main__":
    unittest.main()
