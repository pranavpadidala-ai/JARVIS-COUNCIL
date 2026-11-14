[CmdletBinding()]
param(
    [ValidateRange(1024, 65535)]
    [int]$Port = 47822,

    [string]$Model
)

$ErrorActionPreference = "Stop"
$token = $null
$secureToken = $null
$tokenPointer = [IntPtr]::Zero

try {
    $secureToken = Read-Host "Paste the JARVIS bridge bearer token" -AsSecureString
    $tokenPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureToken)
    $token = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($tokenPointer)

    if ([string]::IsNullOrWhiteSpace($token)) {
        throw "A bridge bearer token is required."
    }

    $baseUri = "http://127.0.0.1:$Port"
    $headers = @{
        Authorization = "Bearer $token"
        Accept = "application/json, text/event-stream"
    }

    Write-Host ""
    Write-Host "Checking JARVIS local development bridge..." -ForegroundColor Cyan
    $health = Invoke-RestMethod -Method Get -Uri "$baseUri/healthz" -Headers $headers
    if ($health.status -ne "online") {
        throw "Bridge health check did not return online."
    }
    Write-Host "[PASS] Authenticated bridge health check" -ForegroundColor Green

    $requestId = 0
    function Invoke-JarvisMcp {
        param(
            [Parameter(Mandatory = $true)][string]$Method,
            [hashtable]$Parameters = @{}
        )

        $script:requestId++
        $body = @{
            jsonrpc = "2.0"
            id = $script:requestId
            method = $Method
            params = $Parameters
        } | ConvertTo-Json -Depth 20 -Compress

        Invoke-RestMethod -Method Post -Uri "$baseUri/mcp" -Headers $headers -ContentType "application/json" -Body $body
    }

    $initialize = Invoke-JarvisMcp -Method "initialize" -Parameters @{
        protocolVersion = "2024-11-05"
        capabilities = @{}
        clientInfo = @{ name = "jarvis-bridge-smoke-test"; version = "1.0" }
    }
    if ($initialize.result.protocolVersion -ne "2024-11-05") {
        throw "MCP initialization returned an unsupported protocol version."
    }
    Write-Host "[PASS] MCP initialization" -ForegroundColor Green

    $tools = Invoke-JarvisMcp -Method "tools/list"
    $toolNames = @($tools.result.tools | ForEach-Object { $_.name })
    foreach ($requiredTool in @("workspace_list", "dotnet_build", "development_task_list", "ollama_status", "ollama_chat")) {
        if ($toolNames -notcontains $requiredTool) {
            throw "Expected MCP tool '$requiredTool' was not advertised."
        }
    }
    Write-Host "[PASS] MCP tool discovery ($($toolNames.Count) tools)" -ForegroundColor Green

    $ollamaCall = Invoke-JarvisMcp -Method "tools/call" -Parameters @{
        name = "ollama_status"
        arguments = @{}
    }
    if ($ollamaCall.result.isError) {
        $detail = $ollamaCall.result.content[0].text
        throw "Ollama is not reachable at 127.0.0.1:11434. $detail"
    }

    $ollama = $ollamaCall.result.content[0].text | ConvertFrom-Json
    $models = @($ollama.models)
    if ($models.Count -eq 0) {
        throw "Ollama is online, but no models are installed."
    }
    Write-Host "[PASS] Ollama is online. Installed models: $($models -join ', ')" -ForegroundColor Green

    $selectedModel = $Model
    if ([string]::IsNullOrWhiteSpace($selectedModel)) {
        $selectedModel = $models | Where-Object { $_ -match "^qwen" } | Select-Object -First 1
    }
    if ([string]::IsNullOrWhiteSpace($selectedModel)) {
        $selectedModel = $models[0]
    }
    if ($models -notcontains $selectedModel) {
        throw "Model '$selectedModel' is not installed. Available models: $($models -join ', ')"
    }

    Write-Host "Testing local model: $selectedModel" -ForegroundColor Cyan
    $chat = Invoke-JarvisMcp -Method "tools/call" -Parameters @{
        name = "ollama_chat"
        arguments = @{
            model = $selectedModel
            prompt = "Reply with exactly this text and nothing else: JARVIS QWEN TEST PASSED"
        }
    }

    if ($chat.result.isError) {
        throw "Local model chat failed: $($chat.result.content[0].text)"
    }

    $answer = $chat.result.content[0].text
    Write-Host ""
    Write-Host "Model response:" -ForegroundColor Cyan
    Write-Host $answer
    if ($answer -notmatch "JARVIS QWEN TEST PASSED") {
        throw "The model responded, but the expected smoke-test phrase was not found."
    }

    Write-Host ""
    Write-Host "[SUCCESS] Bridge, MCP tools, Ollama, and local model chat are working." -ForegroundColor Green
}
catch {
    Write-Error $_
    exit 1
}
finally {
    if ($tokenPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($tokenPointer)
    }
    if ($secureToken) {
        $secureToken.Dispose()
    }
    $token = $null
}
