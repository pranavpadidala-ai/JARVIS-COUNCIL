# First-time JARVIS setup on Windows

This is the beginner-friendly installation path for the Windows desktop build. Git is not required.

## Option A: One PowerShell command (recommended)

Because this is a **private GitHub repository**, the command first installs GitHub CLI if needed and opens GitHub's official browser sign-in flow. It does not ask you to paste a password or token into PowerShell.

Open PowerShell and paste this command:

```powershell
$ErrorActionPreference='Stop'; if(-not(Get-Command gh -ErrorAction SilentlyContinue)){winget install --id GitHub.cli --exact --accept-package-agreements --accept-source-agreements; if($LASTEXITCODE -ne 0){throw 'GitHub CLI installation failed. Install GitHub CLI and rerun this command.'}; $env:Path=[Environment]::GetEnvironmentVariable('Path','Machine')+';'+[Environment]::GetEnvironmentVariable('Path','User')}; gh auth status *> $null; if($LASTEXITCODE -ne 0){gh auth login --hostname github.com --git-protocol https --web; if($LASTEXITCODE -ne 0){throw 'GitHub sign-in failed.'}}; $target=Join-Path $HOME 'JARVIS'; if(Test-Path $target){throw "The folder $target already exists. Rename it first to avoid overwriting files."}; gh repo clone pranavpadidala-ai/JARVIS-COUNCIL $target -- --branch feature/powershell-install-desktop-shortcut; if($LASTEXITCODE -ne 0){throw 'JARVIS download failed.'}; & (Join-Path $target 'Setup-JARVIS.ps1')
```

After GitHub sign-in, it downloads the project to `%USERPROFILE%\\JARVIS`, checks/installs .NET 8, restores dependencies, builds the solution, runs tests, creates a **JARVIS** shortcut on your Windows Desktop, and starts the app. It stops rather than overwriting an existing installation folder.

## Option B: Download ZIP manually

1. Download the ZIP for branch **feature/powershell-install-desktop-shortcut** while signed in to GitHub.
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
