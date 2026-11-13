# First-time JARVIS setup on Windows

This is the beginner-friendly installation path for the Windows desktop build.

## 1. Download the project

1. Open the repository on GitHub.
2. Select the branch **feature/windows-one-click-setup**.
3. Choose **Code → Download ZIP**.
4. Extract the ZIP to a folder you can find again, such as `Documents\JARVIS`.

Git is not required for this installation method.

## 2. Install and verify JARVIS

Double-click **Setup-JARVIS.bat** in the extracted folder. It will:

- Check for the .NET 8 SDK and install it if missing (using winget or Microsoft's official installer).
- Restore project dependencies.
- Build the full solution in Release mode.
- Run the automated test suite.
- Start JARVIS if all checks pass.

The first run needs an internet connection. Setup writes a diagnostic log named `jarvis-setup.log` in the project folder.

## 3. Start JARVIS later

Double-click **Start-JARVIS.bat**. You do not need to repeat setup unless the project is updated or the build is damaged.

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
