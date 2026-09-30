[CmdletBinding()]
param(
    [string]$ApiKeyFile,
    [string]$PromptFile,
    [string]$Prompt,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath,
    [string]$Model = 'gpt-image-2',
    [ValidatePattern('^\d+x\d+$')]
    [string]$Size = '1024x1536',
    [ValidateSet('low', 'medium', 'high', 'auto')]
    [string]$Quality = 'high',
    [ValidateSet('png', 'jpeg', 'webp')]
    [string]$OutputFormat = 'png',
    [ValidateRange(1, 1)]
    [int]$N = 1,
    [int]$TimeoutSec = 240,
    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ApiKeyFile)) {
    $ApiKeyFile = $env:LIKEAI_IMAGE_API_KEY_FILE
}

if ([string]::IsNullOrWhiteSpace($ApiKeyFile)) {
    throw 'Provide -ApiKeyFile or set LIKEAI_IMAGE_API_KEY_FILE. Keep the key outside the repository.'
}

if (-not (Test-Path -LiteralPath $ApiKeyFile -PathType Leaf)) {
    throw "API key file does not exist: $ApiKeyFile"
}

if ((-not [string]::IsNullOrWhiteSpace($PromptFile)) -and (-not [string]::IsNullOrWhiteSpace($Prompt))) {
    throw 'Provide either -PromptFile or -Prompt, not both.'
}

if (-not [string]::IsNullOrWhiteSpace($PromptFile)) {
    if (-not (Test-Path -LiteralPath $PromptFile -PathType Leaf)) {
        throw "Prompt file does not exist: $PromptFile"
    }

    $Prompt = Get-Content -Raw -Encoding UTF8 -LiteralPath $PromptFile
}

if ([string]::IsNullOrWhiteSpace($Prompt)) {
    throw 'Provide -PromptFile or -Prompt.'
}

$outputFullPath = [System.IO.Path]::GetFullPath($OutputPath)
if ((Test-Path -LiteralPath $outputFullPath) -and (-not $Force)) {
    throw "Output already exists. Use -Force only when replacement is intentional: $outputFullPath"
}

$parent = [System.IO.Path]::GetDirectoryName($outputFullPath)
if (-not [string]::IsNullOrWhiteSpace($parent)) {
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
}

$apiKey = (Get-Content -Raw -Encoding UTF8 -LiteralPath $ApiKeyFile).Trim()
if ([string]::IsNullOrWhiteSpace($apiKey)) {
    throw 'API key file is empty.'
}

$payload = [ordered]@{
    model = $Model
    prompt = $Prompt.Trim()
    size = $Size
    quality = $Quality
    output_format = $OutputFormat
    response_format = 'b64_json'
    background = 'opaque'
    n = $N
} | ConvertTo-Json -Depth 5

try {
    $response = Invoke-RestMethod `
        -Uri 'https://api.likeai520.cc/v1/images/generations' `
        -Method Post `
        -Headers @{ Authorization = "Bearer $apiKey" } `
        -ContentType 'application/json' `
        -Body ([System.Text.Encoding]::UTF8.GetBytes($payload)) `
        -TimeoutSec $TimeoutSec

    $b64 = $response.data[0].b64_json
    if ([string]::IsNullOrWhiteSpace($b64)) {
        throw 'API response did not contain data[0].b64_json.'
    }

    [System.IO.File]::WriteAllBytes($outputFullPath, [Convert]::FromBase64String($b64))
    Write-Output "Generated image: $outputFullPath"
    Write-Output ("Bytes: {0}" -f (Get-Item -LiteralPath $outputFullPath).Length)
}
finally {
    $apiKey = $null
}
