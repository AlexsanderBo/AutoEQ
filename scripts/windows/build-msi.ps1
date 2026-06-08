param(
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$Publisher = "AutoEQ",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

if ([string]::IsNullOrWhiteSpace($Version)) {
    [xml]$props = Get-Content (Join-Path $repoRoot "Directory.Build.props")
    $Version = $props.Project.PropertyGroup.Version
}

$publishDir = Join-Path $repoRoot "build\windows\msi-publish"
$distDir = Join-Path $repoRoot "dist\windows"
New-Item -ItemType Directory -Force -Path $distDir | Out-Null

& (Join-Path $PSScriptRoot "publish-windows.ps1") `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -OutputDir $publishDir `
    -SingleFile
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$wixProj = Join-Path $repoRoot "packaging\windows\AutoEQ.Installer.wixproj"
$publishDirForWix = (Resolve-Path $publishDir).Path.TrimEnd('\') + '\'
$distDirForMsBuild = (Resolve-Path $distDir).Path.TrimEnd('\') + '\'

dotnet build $wixProj `
    -c $Configuration `
    /p:PublishDir="$publishDirForWix" `
    /p:ProductVersion="$Version" `
    /p:Publisher="$Publisher" `
    /p:OutputPath="$distDirForMsBuild"
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "MSI output folder: $distDir"
