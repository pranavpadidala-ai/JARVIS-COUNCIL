# Local JARVIS Development Bridge

Status: first implementation in progress.

## Purpose

Expose a small, authenticated Model Context Protocol (MCP) endpoint for a local development client to inspect and validate the JARVIS repository. This is separate from the phone command bridge and does not change the desktop UI.

## Start on Windows

From the repository root:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\scripts\Start-JarvisDevBridge.ps1
```

The script generates a fresh 256-bit bearer token for that run and starts the bridge on:

- MCP endpoint: `http://127.0.0.1:47822/mcp`
- Health endpoint: `http://127.0.0.1:47822/healthz`

The token is printed once so it can be entered into a compatible local MCP client. Keep it private. Stopping the bridge invalidates the token; the next start generates a new one.

## Available tools

| Tool | Permission |
|---|---|
| `workspace_list` | Read-only directory listing; hides Git/build folders |
| `workspace_read_file` | Read-only, size-limited source/documentation files |
| `git_status` | Read-only Git working-tree status |
| `dotnet_restore` | Fixed solution restore |
| `dotnet_build` | Fixed Release solution build |
| `dotnet_test` | Fixed Release solution test |
| `development_task_create` | Adds a task to the existing JARVIS queue; does not approve or execute it |
| `ollama_status` | Checks local Ollama and lists installed models |
| `ollama_chat` | Sends a prompt to an installed local Ollama model and returns its response |

The Ollama tools connect only to `127.0.0.1:11434`; chat is limited to models reported as installed by Ollama and prompts up to 16,000 characters. No cloud model API is used for this path. The bridge does not accept arbitrary shell commands or arbitrary executable paths. File reads are restricted to the configured repository root, traversal and existing symlink/junction paths are rejected, and file extensions are allowlisted. Build/test output is truncated to a bounded size and each process has a ten-minute timeout.

## Network and trust boundary

The listener is hard-bound to IPv4 loopback. It is not reachable from another computer, the LAN, the public internet, or ChatGPT's cloud. Do not change this by port-forwarding the service or publishing it through an unauthenticated tunnel.

A remote ChatGPT connection is a separate integration milestone. It requires a supported remote MCP connector and a secure, authenticated relay with TLS and revocation. A local MCP endpoint alone does not grant this chat access to the Windows machine. Until that is configured, this bridge can be used by a compatible MCP client running on the same PC.

## Current limitations

- No write-file tool yet. Code changes should continue through reviewed Git changes until a staged-write/approval workflow is implemented.
- No unrestricted PowerShell or desktop mouse/keyboard control.
- The existing JARVIS UI is unchanged.
- The bridge does not start automatically; the user explicitly launches it.
