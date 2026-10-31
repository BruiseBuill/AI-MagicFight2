[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$uvPath = Join-Path $env:USERPROFILE '.local/bin/uv.exe'
$constraints = Join-Path $PSScriptRoot 'constraints-win-py312.txt'
if (-not (Test-Path -LiteralPath $uvPath)) { throw "uv not found: $uvPath" }

# Install a persistent tool environment: uvx then reuses it instead of resolving new dependencies.
& $uvPath tool install --offline --python 3.12 --constraints $constraints 'mcpforunityserver==10.1.2'
if ($LASTEXITCODE -ne 0) {
    throw 'MCP repair failed. Check uv cache and any pending MCP dependency download before retrying.'
}
Write-Host 'MCP 10.1.2 installed with pinned dependencies. Run Start-UnityMcp.ps1, then Tools/MCP/Reconnect Existing Server in Unity.'
