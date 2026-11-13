# First-time JARVIS setup on Windows

This is the beginner-friendly installation path for the Windows desktop build. Git is not required.

## Option A: One PowerShell command (recommended)

Open PowerShell and paste this command:

```powershell
irm https://raw.githubusercontent.com/pranavpadidala-ai/JARVIS-COUNCIL/feature/powershell-install-desktop-shortcut/Install-JARVIS.ps1 | iex
```

The installer downloads the project to `%USERPROFILE%\\JARVIS`, checks/installs .NET 8, restores dependencies, builds the solution, runs tests, creates a **JARVIS** shortcut on your Windows Desktop, and starts the app. It stops rather than overwriting an existing `%USERPROFILE%\\JARVIS` folder.

## Option B: Download ZIP manually

1. Download the ZIP for branch **feature/powershell-install-desktop-shortcut**.
2. Extract it to a folder you can find again.
3. Run **Setup-JARVIS.bat** in the extracted folder.

The setup script will:

- Check for the .NET 8 SDK and install it if missing (using winget or Microsoft's official installer).
- Restore project dependencies.
- Build the full solution in Release mode.
- Run the automated test suite.
- Start JARVIS if all checks pass.

The first run needs an internet connection. Setup writes a diagnostic log named `jarvis-setup.log` in the project folder.

## Start JARVIS later

Double-click the **JARVIS** icon on your Desktop. You can also double-click **Start-JARVIS.bat** inside the installation folder. You do not need to repeat setup unless the project is updated or the build is damaged.

## AI provider and costs

If no provider is configured, the app can start in mock mode for local testing. To get real AI responses, configure a supported provider using the environment variables documented in the main README (for example, Gemini). Do not paste API keys into source files, commit them, or share them in chat.

The installer does **not** automatically download local AI models. That is intentional: the model stack is several gigabytes and should be an optional step, especially on an 8 GB RAM laptop. It also does not enable any paid provider or subscription.

## If setup fails

- Check `jarvis-setup.log`.
- Confirm that Windows can access GitHub and NuGet.
- Restart Windows if .NET was installed but PowerShell cannot find it, then run setup again.
- Share the final error text and the log if you need help diagnosing it.

## Validation boundary

The setup script is committed to the repository but has not yet been run on your personal laptop. A successful GitHub commit is not proof that the Windows installation works on your device. The first local setup will provide that confirmation.
