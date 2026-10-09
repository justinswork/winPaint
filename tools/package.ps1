# Builds the Microsoft Store package: a self-contained publish for x64 and ARM64, one .msix per architecture, and an
# .msixbundle to upload in Partner Center. The Store signs the package; no certificate is needed for submission.
#
# Usage (values from Partner Center -> your app -> Product management -> Product identity):
#   .\tools\package.ps1 -IdentityName "12345Publisher.winPaint" -Publisher "CN=XXXXXXXX-XXXX-XXXX-XXXX-XXXXXXXXXXXX" `
#                       -PublisherDisplayName "Your Publisher Name" -Version 1.0.0.0
param(
    # Must equal the Identity Name Partner Center assigned to the winPaint reservation.
    [string]$IdentityName = "JustinKing.winPaint",
    # Same for every app in the Partner Center account (taken from MarkdownStudio's Store identity).
    [string]$Publisher = "CN=7FF40E1D-C390-4E69-A012-910F68FFFA3A",
    [string]$PublisherDisplayName = "Justin King",
    [string]$Version = "1.0.0.0",
    [string[]]$Architectures = @("x64", "arm64")
)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root

if ($Version -notmatch '^\d+\.\d+\.\d+\.0$') { throw "Version must have four parts and end in .0 (Store requirement), e.g. 1.0.0.0" }
$sdk = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Directory | Where-Object { $_.Name -match '^10\.' } | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$makeappx = Join-Path $sdk.FullName "x64\makeappx.exe"
$makepri = Join-Path $sdk.FullName "x64\makepri.exe"
if (-not (Test-Path $makeappx)) { throw "makeappx.exe not found (install the Windows SDK)." }

$out = Join-Path $root "artifacts\package"
$work = Join-Path $out "work"
if (Test-Path $work) { Remove-Item $work -Recurse -Force }
New-Item -ItemType Directory -Force $work | Out-Null
$bundleInput = Join-Path $work "bundle"
New-Item -ItemType Directory -Force $bundleInput | Out-Null
$template = [IO.File]::ReadAllText((Join-Path $root "packaging\Package.appxmanifest"))
$assemblyVersion = ($Version -split '\.')[0..2] -join '.'

foreach ($arch in $Architectures) {
    Write-Host "== Publishing $arch"
    $layout = Join-Path $work "layout-$arch"
    dotnet publish src/WinPaint.App/WinPaint.App.csproj -c Release -r "win-$arch" --self-contained true `
        -p:Version=$assemblyVersion -p:PublishSingleFile=false -o $layout --nologo -v:minimal
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $arch" }

    Copy-Item (Join-Path $root "packaging\Images") (Join-Path $layout "Images") -Recurse -Force
    $manifest = $template.Replace('$IDENTITY_NAME$', [Security.SecurityElement]::Escape($IdentityName)).
        Replace('$PUBLISHER$', [Security.SecurityElement]::Escape($Publisher)).
        Replace('$PUBLISHER_DISPLAY_NAME$', [Security.SecurityElement]::Escape($PublisherDisplayName)).
        Replace('$VERSION$', $Version).
        Replace('$ARCH$', $arch)
    [IO.File]::WriteAllText((Join-Path $layout "AppxManifest.xml"), $manifest, (New-Object Text.UTF8Encoding($false)))

    # Debug symbols are not shipped in the package.
    Get-ChildItem $layout -Filter *.pdb -Recurse | Remove-Item -Force

    # Index the scale/target-size qualified logos into resources.pri (MRT) so the manifest's plain names resolve.
    $priConfig = Join-Path $work "priconfig-$arch.xml"
    & $makepri createconfig /cf $priConfig /dq en-US /pv 10.0.0 /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makepri createconfig failed" }
    & $makepri new /pr $layout /cf $priConfig /mn (Join-Path $layout "AppxManifest.xml") /of (Join-Path $layout "resources.pri") /o | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "makepri new failed for $arch" }

    $msix = Join-Path $bundleInput "winPaint_${Version}_$arch.msix"
    & $makeappx pack /d $layout /p $msix /o /h SHA256 | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed for $arch" }
}

$bundle = Join-Path $out "winPaint_$Version.msixbundle"
& $makeappx bundle /d $bundleInput /p $bundle /bv $Version /o | Out-Host
if ($LASTEXITCODE -ne 0) { throw "makeappx bundle failed" }
# .msixupload = zip containing the bundle (same upload format as MarkdownStudio's releases).
$upload = Join-Path $out "winPaint_$Version.msixupload"
if (Test-Path $upload) { Remove-Item $upload -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip = [IO.Compression.ZipFile]::Open($upload, 'Create')
[void][IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $bundle, (Split-Path $bundle -Leaf), 'Optimal')
$zip.Dispose()
Remove-Item $work -Recurse -Force
Write-Host ""
Write-Host "Identity: $IdentityName / $Publisher / $PublisherDisplayName / $Version"
Write-Host "Upload this file in Partner Center -> your winPaint submission -> Packages:"
Write-Host "  $upload"
