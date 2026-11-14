#!/usr/bin/env python3
"""JARVIS Python capability worker.

One JSON request is read from stdin and one JSON response is written to stdout.
Only fixed, non-executing inspection actions are exposed. User source is parsed,
never imported or executed.
"""
from __future__ import annotations

import ast
import importlib.util
import json
import os
import platform
import shutil
import sys
from pathlib import Path
from typing import Any

MAX_REQUEST_BYTES = 1_000_000
MAX_SOURCE_BYTES = 512_000
MAX_FILE_BYTES = 512_000

MODULES = (
    "numpy", "scipy", "pandas", "sympy", "torch", "tensorflow",
    "transformers", "ollama", "openai", "langchain", "langgraph",
    "chromadb", "faiss", "cv2", "PIL", "easyocr", "pytesseract",
    "playwright", "selenium", "pyautogui", "pywinauto", "uiautomation",
    "ezdxf", "ifcopenshell", "openseespy", "win32com", "fastapi",
    "flask", "streamlit", "gradio", "docx", "openpyxl", "pptx",
    "pypdf", "fitz", "pdfplumber", "reportlab", "sounddevice",
    "faster_whisper", "onnxruntime", "trimesh", "networkx", "piper",
    "sentencepiece", "sherpa_onnx",
)

ENGINEERING_COMMANDS = (
    "abaqus", "abq2020", "abq2024", "matlab", "ETABS", "SAP2000",
    "SAFE", "staad", "acad", "msbuild", "devenv",
)


def _ok(action: str, message: str, data: Any = None) -> dict[str, Any]:
    return {"success": True, "action": action, "message": message, "data": data}


def _fail(action: str, message: str, data: Any = None) -> dict[str, Any]:
    return {"success": False, "action": action, "message": message, "data": data}


def _workspace(request: dict[str, Any]) -> Path:
    value = request.get("workspace")
    root = Path(value).expanduser().resolve() if isinstance(value, str) and value else Path.cwd().resolve()
    if not root.is_dir():
        raise ValueError("The configured JARVIS workspace does not exist.")
    return root


def _safe_file(request: dict[str, Any]) -> Path:
    raw = request.get("path")
    if not isinstance(raw, str) or not raw.strip() or len(raw) > 400:
        raise ValueError("A workspace-relative Python file path is required (maximum 400 characters).")
    requested = Path(raw)
    if requested.is_absolute():
        raise ValueError("Absolute paths are not accepted; use a path relative to the JARVIS workspace.")
    root = _workspace(request)
    candidate = (root / requested).resolve(strict=True)
    if os.path.commonpath((os.path.normcase(str(root)), os.path.normcase(str(candidate)))) != os.path.normcase(str(root)):
        raise ValueError("The requested path resolves outside the JARVIS workspace.")
    if not candidate.is_file():
        raise ValueError("The requested path is not a regular file.")
    if candidate.stat().st_size > MAX_FILE_BYTES:
        raise ValueError("The Python file exceeds the 512 KB inspection limit.")
    if candidate.suffix.lower() != ".py":
        raise ValueError("Only .py files can be inspected by this action.")
    return candidate


def _source_from_request(request: dict[str, Any]) -> tuple[str, str]:
    source = request.get("source")
    path = request.get("path")
    if isinstance(source, str) and isinstance(path, str):
        raise ValueError("Supply either source or path, not both.")
    if isinstance(source, str):
        if len(source.encode("utf-8")) > MAX_SOURCE_BYTES:
            raise ValueError("Python source exceeds the 512 KB limit.")
        return source, "<provided-source>"
    if isinstance(path, str):
        file_path = _safe_file(request)
        return file_path.read_text(encoding="utf-8-sig"), str(file_path)
    raise ValueError("Supply Python source text or a workspace-relative .py file path.")


def _inspect_ast(source: str, filename: str) -> dict[str, Any]:
    tree = ast.parse(source, filename=filename, mode="exec")
    imports: list[dict[str, Any]] = []
    functions: list[dict[str, Any]] = []
    classes: list[dict[str, Any]] = []

    for node in ast.walk(tree):
        if isinstance(node, ast.Import):
            for alias in node.names:
                imports.append({"module": alias.name, "line": node.lineno})
        elif isinstance(node, ast.ImportFrom):
            imports.append({"module": node.module or "", "line": node.lineno})
        elif isinstance(node, (ast.FunctionDef, ast.AsyncFunctionDef)):
            functions.append({
                "name": node.name,
                "line": node.lineno,
                "async": isinstance(node, ast.AsyncFunctionDef),
            })
        elif isinstance(node, ast.ClassDef):
            classes.append({"name": node.name, "line": node.lineno})

    return {
        "filename": filename,
        "line_count": len(source.splitlines()),
        "imports": sorted(imports, key=lambda item: (item["line"], item["module"])),
        "functions": sorted(functions, key=lambda item: item["line"]),
        "classes": sorted(classes, key=lambda item: item["line"]),
    }


def handle(request: dict[str, Any]) -> dict[str, Any]:
    if not isinstance(request, dict):
        return _fail("unknown", "The request must be a JSON object.")

    action = request.get("action")
    if not isinstance(action, str):
        return _fail("unknown", "A string action is required.")
    action = action.strip().lower()

    try:
        if action == "health":
            return _ok("health", "Python worker is ready.", {
                "python": platform.python_version(),
                "executable": sys.executable,
                "platform": platform.platform(),
                "implementation": platform.python_implementation(),
            })

        if action == "inspect_modules":
            results: dict[str, bool] = {}
            for name in MODULES:
                try:
                    results[name] = importlib.util.find_spec(name) is not None
                except (ImportError, ModuleNotFoundError, ValueError):
                    results[name] = False
            return _ok("inspect_modules", "Checked the allowlisted Python module set.", {
                "executable": sys.executable,
                "modules": results,
                "installed_count": sum(results.values()),
                "checked_count": len(results),
            })

        if action == "engineering_tools":
            available = {
                name: shutil.which(name) is not None
                for name in ENGINEERING_COMMANDS
            }
            return _ok("engineering_tools", "Checked engineering and development command availability.", {
                "commands": available,
            })

        if action == "check_syntax":
            source, filename = _source_from_request(request)
            try:
                compile(source, filename, "exec", dont_inherit=True)
            except SyntaxError as exc:
                return _fail("check_syntax", f"Python syntax error: {exc.msg}", {
                    "filename": exc.filename,
                    "line": exc.lineno,
                    "column": exc.offset,
                    "source_line": exc.text.rstrip("\n") if exc.text else None,
                })
            return _ok("check_syntax", "Python syntax is valid. The source was compiled for checking but not executed.", {
                "filename": filename,
                "line_count": len(source.splitlines()),
                "executed": False,
            })

        if action == "inspect_file":
            source, filename = _source_from_request(request)
            try:
                details = _inspect_ast(source, filename)
            except SyntaxError as exc:
                return _fail("inspect_file", f"Python syntax error: {exc.msg}", {
                    "filename": exc.filename,
                    "line": exc.lineno,
                    "column": exc.offset,
                })
            return _ok("inspect_file", "Parsed the Python file without importing or executing it.", details)

        return _fail(action, "Unsupported action. Available actions: health, inspect_modules, engineering_tools, check_syntax, inspect_file.")
    except (OSError, UnicodeError, ValueError) as exc:
        return _fail(action, str(exc))


def main() -> int:
    line = sys.stdin.buffer.readline(MAX_REQUEST_BYTES + 1)
    if not line:
        response = _fail("unknown", "No JSON request was supplied.")
    elif len(line) > MAX_REQUEST_BYTES:
        response = _fail("unknown", "Request exceeds the 1 MB limit.")
    else:
        try:
            request = json.loads(line.decode("utf-8"))
            response = handle(request)
        except (UnicodeDecodeError, json.JSONDecodeError) as exc:
            response = _fail("unknown", f"Invalid JSON request: {exc}")

    sys.stdout.write(json.dumps(response, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
