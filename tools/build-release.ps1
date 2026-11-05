<#
    Builds the downloadable release:

      dist/AetherLauncher-<Version>-<Runtime>/AetherLauncher.exe   one file, no installer
      dist/AetherLauncher-<Version>-<Runtime>.zip                  upload artifact
      dist/AetherLauncher-<Version>-<Runtime>.zip.sha256           checksum

    Release hardening:
      * no .pdb files or dependency XML documentation in the download
      * single-file and ReadyToRun output
      * SHA-256 beside the archive so a download can be verified
      * optional Authenticode signature with -CertificatePath

    No build step makes a .NET application impossible to inspect. Encryption protects
    stored account keys, not the code itself. Aether keeps no passwords or API secrets
    in the binary. Account cookies use AES-256-GCM with a DPAPI-bound key; see Dpapi.cs.

    Usage:
      powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-release.ps1
      ... -Version 1.2.0
      ... -SelfContained                      (bundles the .NET runtime, ~70 MB)
      ... -CertificatePath .\aether.pfx       (prompts for the password)
#>

param(
    [string]$Version = '1.2.0',
    [string]$Runtime = 'win-x64',
    [switch]$SelfContained,
    [string]$CertificatePath = '',
    [SecureString]$CertificatePassword,
    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'src/AetherLauncher/AetherLauncher.csproj'
$name = "AetherLauncher-$Version-$Runtime"
$distRoot = Join-Path $root 'dist'
$dist = Join-Path $distRoot $name
$zip = Join-Path $distRoot "$name.zip"

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

# Remove the previous build of this version so no stale files are uploaded.
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
if (Test-Path $zip) { Remove-Item $zip -Force }
New-Item -ItemType Directory -Path $dist -Force | Out-Null

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', $Runtime,
    '--self-contained', $(if ($SelfContained) { 'true' } else { 'false' }),
    "-p:Version=$Version",
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:PublishReadyToRun=true',
    '-p:DebugType=None',
    '-p:DebugSymbols=false',
    '-p:PublishReferencesDocumentationFiles=false',
    '-o', $dist
)

Write-Host "Building $name ..." -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

# Remove symbols or docs if the SDK copied them despite the switches.
Get-ChildItem $dist -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $dist -Filter *.xml -File -ErrorAction SilentlyContinue | Remove-Item -Force

$exe = Join-Path $dist 'AetherLauncher.exe'
if (-not (Test-Path $exe)) { throw "The output does not contain AetherLauncher.exe." }

# ---------------------------------------------------------------- Signing (optional)
$signed = $false
if (-not [string]::IsNullOrWhiteSpace($CertificatePath)) {
    $cert = (Resolve-Path $CertificatePath).Path
    $signtool = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if (-not $signtool) {
        $signtool = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
            Where-Object { $_.FullName -match '\\x64\\' } |
            Sort-Object FullName -Descending |
            Select-Object -First 1
    }
    if (-not $signtool) {
        throw "signtool.exe was not found. Install the Windows SDK or sign the file on the build machine."
    }

    if (-not $CertificatePassword) {
        $CertificatePassword = Read-Host -Prompt "Password for $([IO.Path]::GetFileName($cert))" -AsSecureString
    }

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        $tool = if ($signtool -is [System.Management.Automation.ApplicationInfo]) { $signtool.Source } else { $signtool.FullName }
        & $tool sign /fd SHA256 /f $cert /p $plain /tr $TimestampUrl /td SHA256 $exe
        if ($LASTEXITCODE -ne 0) { throw "signtool sign failed with exit code $LASTEXITCODE." }

        & $tool verify /pa $exe
        if ($LASTEXITCODE -ne 0) { throw "The signature could not be verified." }

        $signed = $true
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

# ---------------------------------------------------------------- Package and checksum
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal -Force

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$hashFile = "$zip.sha256"
# LF instead of CRLF so sha256sum -c can verify the file.
[IO.File]::WriteAllText($hashFile, "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.Encoding]::ASCII)

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)

Write-Host ''
Write-Host "Done: $zip ($size MB)" -ForegroundColor Green
Write-Host "SHA-256: $hash"
Write-Host "Signed: $(if ($signed) { 'yes' } else { 'no (-CertificatePath not set)' })"
Write-Host ''
Write-Host 'Verify on another machine:'
Write-Host "  certutil -hashfile $([IO.Path]::GetFileName($zip)) SHA256"
Write-Host "  Get-AuthenticodeSignature .\AetherLauncher.exe"
