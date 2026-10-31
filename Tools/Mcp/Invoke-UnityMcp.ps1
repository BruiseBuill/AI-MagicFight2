[CmdletBinding()]
param(
    [ValidateSet('tools/call', 'tools/list', 'resources/read')]
    [string]$Method = 'tools/call',
    [string]$Name,
    [string]$Arguments = '{}',
    [string]$CodeFile,
    [string]$Uri
)
$ErrorActionPreference = 'Stop'
$headers = @{ Accept = 'application/json, text/event-stream' }
$endpoint = 'http://127.0.0.1:8080/mcp'
function Send-Rpc($request) {
    $body = $request | ConvertTo-Json -Depth 40 -Compress
    Invoke-WebRequest $endpoint -Method Post -ContentType 'application/json' -Headers $headers -Body ([System.Text.Encoding]::UTF8.GetBytes($body)) -TimeoutSec 55
}
$init = Send-Rpc @{jsonrpc='2.0';id=1;method='initialize';params=@{protocolVersion='2024-11-05';capabilities=@{};clientInfo=@{name='project-tools';version='1.0'}}}
$headers['Mcp-Session-Id'] = [string]($init.Headers['mcp-session-id'] | Select-Object -First 1)
try {
    $null = Send-Rpc @{jsonrpc='2.0';method='notifications/initialized'}
    $params = @{}
    if ($Method -eq 'tools/call') {
        $argsObject = $Arguments | ConvertFrom-Json -AsHashtable
        if ($CodeFile) { $argsObject['code'] = Get-Content -LiteralPath $CodeFile -Raw }
        $params = @{name=$Name;arguments=$argsObject}
    } elseif ($Method -eq 'resources/read') { $params = @{uri=$Uri} }
    $response = Send-Rpc @{jsonrpc='2.0';id=2;method=$Method;params=$params}
    foreach ($line in ($response.Content -split "`n")) {
        if (-not $line.StartsWith('data: ')) { continue }
        $message = $line.Substring(6) | ConvertFrom-Json
        if ($message.id -ne 2) { continue }
        if ($message.error) { throw ($message.error | ConvertTo-Json -Compress) }
        $message.result | ConvertTo-Json -Depth 40
    }
} finally {
    try { $null = Invoke-WebRequest $endpoint -Method Delete -Headers $headers -TimeoutSec 5 } catch { }
}
