[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$uvxPath = 'D:\Python\Scripts\uvx.exe'
if (-not (Test-Path -LiteralPath $uvxPath)) { throw "uvx not found: $uvxPath" }
$listeners = Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue
if ($listeners) {
    Write-Host 'Port 8080 already has a listener. Check MCP instances/project/info/editor/state before starting another server.'
    return
}
& $uvxPath --offline --from 'mcpforunityserver==10.1.2' mcp-for-unity --transport http --http-url 'http://127.0.0.1:8080' --project-scoped-tools
if ($LASTEXITCODE -ne 0) { throw "MCP exited with code $LASTEXITCODE" }
