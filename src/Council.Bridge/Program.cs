using Council.Bridge;

var workspaceRoot = Environment.GetEnvironmentVariable("JARVIS_WORKSPACE_ROOT")
    ?? Directory.GetCurrentDirectory();
var token = Environment.GetEnvironmentVariable("JARVIS_DEV_BRIDGE_TOKEN");
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("JARVIS_DEV_BRIDGE_TOKEN is required. Start the bridge with scripts/Start-JarvisDevBridge.ps1.");
    return 2;
}

var port = int.TryParse(Environment.GetEnvironmentVariable("JARVIS_DEV_BRIDGE_PORT"), out var configuredPort)
    ? configuredPort
    : 47822;

using var shutdown = new CancellationTokenSource();
ConsoleCancelEventHandler cancelHandler = (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Cancel();
};
Console.CancelKeyPress += cancelHandler;

try
{
    await using var server = new LocalMcpServer(workspaceRoot, token, port);
    server.Start();
    Console.WriteLine("JARVIS local development bridge is running.");
    Console.WriteLine($"MCP endpoint: http://127.0.0.1:{port}/mcp");
    Console.WriteLine($"Workspace: {Path.GetFullPath(workspaceRoot)}");
    Console.WriteLine("Loopback only. No public or LAN interface is exposed. Press Ctrl+C to stop.");
    await Task.Delay(Timeout.Infinite, shutdown.Token);
}
catch (OperationCanceledException) when (shutdown.IsCancellationRequested)
{
}
finally
{
    Console.CancelKeyPress -= cancelHandler;
}

return 0;
