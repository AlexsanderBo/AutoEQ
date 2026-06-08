param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$OutputDir = "",
    [switch]$SingleFile
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    $OutputDir = Join-Path $repoRoot "build\windows\publish"
}

$project = Join-Path $repoRoot "AutoEQ\AutoEQ.csproj"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null
Get-ChildItem -LiteralPath $OutputDir -Force | Remove-Item -Recurse -Force

$publishArgs = @(
    "publish", $project,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-o", $OutputDir
)

if ($SingleFile) {
    $publishArgs += @(
        "/p:PublishSingleFile=true",
        "/p:IncludeNativeLibrariesForSelfExtract=true",
        "/p:EnableCompressionInSingleFile=true"
    )
} else {
    $publishArgs += "/p:PublishSingleFile=false"
}

dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item -Force (Join-Path $repoRoot "README.md") (Join-Path $OutputDir "README.md")

$note = @"
Run AutoEQ.exe as Administrator the first time so it can add:
Include: AutoEQ_autoeq.txt

to:
C:\Program Files\EqualizerAPO\config\config.txt

After that, normal user mode should be enough for monitoring.
"@
Set-Content -Path (Join-Path $OutputDir "run_as_admin_note.txt") -Value $note -Encoding UTF8

Write-Host "Windows publish output: $OutputDir"
