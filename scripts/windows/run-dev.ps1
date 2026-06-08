$ErrorActionPreference = "Stop"
$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..\..")

dotnet restore (Join-Path $repoRoot "AutoEQ.sln")
dotnet run --project (Join-Path $repoRoot "AutoEQ\AutoEQ.csproj")
