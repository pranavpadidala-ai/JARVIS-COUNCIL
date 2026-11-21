using System.Net;
using System.Net.Sockets;
using Council.App.Chat;
using Xunit;

namespace Council.Dev.Tests;

public sealed class OmniRouteLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jarvis-omni-" + Guid.NewGuid().ToString("N"));

    public OmniRouteLauncherTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public void DoesNothingWhenAnotherProviderIsSelected()
    {
        var launcher = new OmniRouteLauncher(null, "http://localhost:1/v1");
        Assert.Null(launcher.EnsureRunning("ollama"));
        Assert.False(launcher.StartedByJarvis);
    }

    [Fact]
    public void LeavesAnAlreadyRunningGatewayAlone()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var launcher = new OmniRouteLauncher(Path.Combine(_root, "never-run.cmd"), $"http://localhost:{port}/v1");
            Assert.Null(launcher.EnsureRunning("omniroute"));
            Assert.False(launcher.StartedByJarvis);
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public void ExplainsAMissingLauncherInsteadOfFailing()
    {
        var launcher = new OmniRouteLauncher(Path.Combine(_root, "missing.cmd"), "http://localhost:1/v1");
        var status = launcher.EnsureRunning("omniroute");
        Assert.Contains("launcher was not found", status);
        Assert.False(launcher.StartedByJarvis);
    }

    [Fact]
    public void IgnoresRemoteGateways()
    {
        var launcher = new OmniRouteLauncher(Path.Combine(_root, "missing.cmd"), "https://gateway.example.com/v1");
        Assert.Null(launcher.EnsureRunning("omniroute"));
    }

    [Fact]
    public void FindsTheLauncherNextToTheRepository()
    {
        var repo = Directory.CreateDirectory(Path.Combine(_root, "JARVIS")).FullName;
        File.WriteAllText(Path.Combine(repo, "JARVIS.Council.sln"), "");
        var bin = Directory.CreateDirectory(Path.Combine(repo, "src", "Council.Desktop", "bin")).FullName;
        var tools = Directory.CreateDirectory(Path.Combine(_root, "JARVIS-tools")).FullName;
        var launcherPath = Path.Combine(tools, "Start-OmniRoute.cmd");

        Assert.Null(OmniRouteLauncher.FindLauncher(bin)); // not installed yet
        File.WriteAllText(launcherPath, "@echo off");
        Assert.Equal(launcherPath, OmniRouteLauncher.FindLauncher(bin));
    }
}
