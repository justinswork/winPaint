# CI-like build: build (warnings are errors) + run all tests.
param(
    [string]$Configuration = "Debug",
    [switch]$SkipUiTests
)
$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot
dotnet build winPaint.sln -c $Configuration
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet test tests/WinPaint.Core.Tests -c $Configuration --no-build
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if (-not $SkipUiTests) {
    dotnet test tests/WinPaint.UiTests -c $Configuration --no-build
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
}
