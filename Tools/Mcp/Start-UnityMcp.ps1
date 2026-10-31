[CmdletBinding()]
param([switch]$Background)
$ErrorActionPreference = 'Stop'
$uvxPath = 'C:\Users\Lenovo\.local\bin\uvx.exe'
if (-not (Test-Path -LiteralPath $uvxPath)) { throw "uvx not found: $uvxPath" }
$listeners = Get-NetTCPConnection -LocalPort 8080 -State Listen -ErrorAction SilentlyContinue
if ($listeners) {
    $health = Invoke-RestMethod 'http://127.0.0.1:8080/health' -TimeoutSec 5
    if ($health.status -ne 'healthy' -or $health.version -ne '10.1.2') {
        throw 'Port 8080 is occupied, but it is not the expected healthy MCP 10.1.2 server.'
    }
    Write-Host 'MCP 10.1.2 is healthy. Verify Unity instances/project/info/editor/state before using tools.'
    return
}
if ($Background) {
    $projectRoot = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
    $logDir = Join-Path $projectRoot 'Library/MCPForUnity/Logs'
    New-Item -ItemType Directory -Path $logDir -Force | Out-Null
    $process = Start-Process -FilePath $uvxPath -ArgumentList @(
        '--offline', '--from', 'mcpforunityserver==10.1.2', 'mcp-for-unity',
        '--transport', 'http', '--http-url', 'http://127.0.0.1:8080', '--project-scoped-tools'
    ) -WorkingDirectory $projectRoot -WindowStyle Hidden -PassThru `
      -RedirectStandardOutput (Join-Path $logDir 'server-start.stdout.log') `
      -RedirectStandardError (Join-Path $logDir 'server-start.stderr.log')
    $deadline = (Get-Date).AddSeconds(25)
    do {
        if ($process.HasExited) { throw "MCP exited. Check $logDir/server-start.stderr.log" }
        try {
            $health = Invoke-RestMethod 'http://127.0.0.1:8080/health' -TimeoutSec 2
            if ($health.status -eq 'healthy' -and $health.version -eq '10.1.2') {
                Write-Host "MCP 10.1.2 is healthy at http://127.0.0.1:8080 (launcher PID $($process.Id))."
                return
            }
        } catch { }
        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)
    throw "MCP did not become healthy. Check $logDir/server-start.stderr.log; repair dependencies with Repair-UnityMcp.ps1."
}
& $uvxPath --offline --from 'mcpforunityserver==10.1.2' mcp-for-unity --transport http --http-url 'http://127.0.0.1:8080' --project-scoped-tools
if ($LASTEXITCODE -ne 0) { throw "MCP exited with code $LASTEXITCODE" }
