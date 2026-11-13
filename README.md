# JARVIS Council

A Windows-first personal AI assistant with a persistent chat, provider failover and an explicitly invoked autonomous development mode.

## Current build

- Interactive console assistant with persistent local conversation history.\n- Windows WPF Command Center desktop shell, styled around the user's futuristic dashboard reference.\n- Desktop chat window connected to the same AI provider fallback and persistent conversation store as the console.\n- Live Windows CPU (JARVIS process), system RAM and system-drive free-space telemetry.
- Gemini, OpenAI and OpenAI-compatible provider support.
- Sequential provider fallback: an unavailable provider does not stop the session if another configured provider works.
- OmniRoute can be used through its OpenAI-compatible `/chat/completions` endpoint.
- Explicit `/dev` command for repository work, with a clean-Git checkpoint, bounded workspace access, build/test validation and rollback.
- API credentials are read from environment variables and are not stored in chat history.

## Requirements

- Windows 10/11
- .NET 8 SDK
- Git
- An API key for at least one configured AI provider

## Run JARVIS

Open PowerShell in the repository folder.

For a one-session Gemini key:

```powershell
$env:GEMINI_API_KEY = Read-Host "Gemini API key"
dotnet run --project src/Council.App
```

Other providers:

```powershell
$env:OPENAI_API_KEY = Read-Host "OpenAI API key"
$env:OMNIROUTE_API_KEY = Read-Host "OmniRoute API key"
$env:OMNIROUTE_BASE_URL = "http://localhost:PORT/v1"
$env:OMNIROUTE_MODEL = "your-model-id"
```

Set `JARVIS_AI_PROVIDER` to `gemini`, `openai`, or `omniroute` to choose the first provider in the failover order. If no key is configured, JARVIS starts in mock/demo mode.

## Commands

| Command | Action |
|---|---|
| `/help` | Show available commands |
| `/status` | Show provider route, workspace and saved task count |
| `/history` | Show recent conversation |
| `/clear` | Clear chat history (development task records remain) |
| `/dev <task>` | Start an explicitly approved development task |
| `/exit` | Save and close JARVIS |

Chat history and development task records are stored under the current Windows user's local application data folder, in `%LOCALAPPDATA%\JARVIS`. They are not committed to the repository.

## Autonomous development safety

The `/dev` command is intentionally separate from ordinary chat. Before it starts, JARVIS asks for the literal confirmation `RUN`. The repository must have a clean Git working tree so the development orchestrator can establish a checkpoint. Changes are validated with `dotnet build` and `dotnet test`; failed attempts are rolled back before retry.

Do not run autonomous development in a folder containing uncommitted work you need to preserve. Keep important work committed or backed up first.

## Configuration

| Variable | Purpose |
|---|---|
| `GEMINI_API_KEY` | Gemini authentication |
| `GEMINI_MODEL` | Optional Gemini model override |
| `OPENAI_API_KEY` | OpenAI authentication |
| `OPENAI_MODEL` | Optional OpenAI model override |
| `OMNIROUTE_API_KEY` | OmniRoute or compatible gateway key |
| `OMNIROUTE_BASE_URL` | Gateway base URL, normally ending in `/v1` |
| `OMNIROUTE_MODEL` | Model identifier accepted by the gateway |
| `JARVIS_AI_PROVIDER` | Preferred first provider: `gemini`, `openai`, or `omniroute` |
| `JARVIS_WORKSPACE` | Optional repository/workspace path for `/dev` |

## Roadmap

See [docs/MASTER_PLAN.md](docs/MASTER_PLAN.md) for the staged architecture and integration plan.

## Current limitations

This build is the usable assistant foundation plus the first desktop Command Center shell, not the final JARVIS experience. Live dashboard integration, graphical desktop control, Abaqus automation, specialist engineering-document parsing, offline speech, Android remote control, Google Drive integration and market-data monitoring are subsequent modules. Real-money trading and unattended broker order placement are not enabled.
