param(
    [string]$Configuration = "Release",
    [string]$Runtime = "linux-x64",
    [string]$OutputDir = ""
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "dist\AutoEQ.Linux"
}

$project = Join-Path $repoRoot "AutoEQ.Linux\AutoEQ.Linux.csproj"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Get-ChildItem -LiteralPath $OutputDir -Force | Remove-Item -Recurse -Force

dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    /p:PublishSingleFile=true `
    -o $OutputDir
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Linux publish output: $OutputDir"
Write-Host "On Ubuntu run: chmod +x autoeq-linux install_ubuntu.sh && ./install_ubuntu.sh"
