using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Council.Bridge;

namespace Council.Dev.Tests;

public sealed class LocalMcpServerTests
{
    [Fact]
    public async Task McpEndpoint_RejectsMissingBearerToken()
    {
        var root = CreateRoot();
        try
        {
            await using var server = new LocalMcpServer(root, new string('x', 48), GetFreePort());
            await server.StartAsync();

            using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{server.Port}") };
            using var response = await client.GetAsync("/healthz");

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task McpEndpoint_InitializesAndListsToolsWithValidToken()
    {
        var root = CreateRoot();
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        try
        {
            await using var server = new LocalMcpServer(root, token, GetFreePort());
            await server.StartAsync();

            using var client = CreateClient(server.Port, token);
            using var initialize = await PostMcpAsync(client,
                """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2024-11-05","capabilities":{},"clientInfo":{"name":"bridge-tests","version":"1.0"}}}""");
            Assert.Equal(HttpStatusCode.OK, initialize.StatusCode);

            using var initializeJson = JsonDocument.Parse(await initialize.Content.ReadAsStringAsync());
            Assert.Equal("2024-11-05", initializeJson.RootElement.GetProperty("result").GetProperty("protocolVersion").GetString());

            using var list = await PostMcpAsync(client,
                """{"jsonrpc":"2.0","id":2,"method":"tools/list","params":{}}""");
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);

            using var listJson = JsonDocument.Parse(await list.Content.ReadAsStringAsync());
            var names = listJson.RootElement.GetProperty("result").GetProperty("tools")
                .EnumerateArray()
                .Select(tool => tool.GetProperty("name").GetString())
                .ToArray();

            Assert.Contains("workspace_read_file", names);
            Assert.Contains("dotnet_build", names);
            Assert.Contains("development_task_create", names);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task McpEndpoint_CanReadWorkspaceFileButCannotReadOutsideWorkspace()
    {
        var root = CreateRoot();
        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray()) + Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        try
        {
            File.WriteAllText(Path.Combine(root, "README.md"), "bridge test content");
            await using var server = new LocalMcpServer(root, token, GetFreePort());
            await server.StartAsync();

            using var client = CreateClient(server.Port, token);
            using var read = await PostMcpAsync(client,
                """{"jsonrpc":"2.0","id":3,"method":"tools/call","params":{"name":"workspace_read_file","arguments":{"path":"README.md"}}}""");
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);

            using var readJson = JsonDocument.Parse(await read.Content.ReadAsStringAsync());
            Assert.Contains("bridge test content",
                readJson.RootElement.GetProperty("result").GetProperty("content")[0].GetProperty("text").GetString());

            using var traversal = await PostMcpAsync(client,
                """{"jsonrpc":"2.0","id":4,"method":"tools/call","params":{"name":"workspace_read_file","arguments":{"path":"../outside.txt"}}}""");
            using var traversalJson = JsonDocument.Parse(await traversal.Content.ReadAsStringAsync());
            Assert.True(traversalJson.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static HttpClient CreateClient(int port, string token)
    {
        var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
        return client;
    }

    private static Task<HttpResponseMessage> PostMcpAsync(HttpClient client, string json) =>
        client.PostAsync("/mcp", new StringContent(json, Encoding.UTF8, "application/json"));

    private static int GetFreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static string CreateRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "jarvis-mcp-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
