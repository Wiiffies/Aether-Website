<#
    Builds the downloadable release:

      dist/AetherLauncher-<Version>-<Runtime>.exe          standalone executable
      dist/AetherLauncher-<Version>-<Runtime>.exe.sha256   checksum

    Release hardening:
      * self-contained: the .NET runtime ships inside the executable
      * single-file and ReadyToRun output
      * SHA-256 beside the executable so a download can be verified
      * optional Authenticode signature with -CertificatePath

    No build step makes a .NET application impossible to inspect. Encryption protects
    stored account keys, not the code itself. Aether keeps no passwords or API secrets
    in the binary. Account cookies use AES-256-GCM with a DPAPI-bound key; see Dpapi.cs.

    Usage:
      powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-release.ps1
      ... -Version 1.2.0
      ... -FrameworkDependent             (smaller build; requires the .NET runtime)
      ... -CertificatePath .\aether.pfx   (prompts for the password)
#>

param(
    [string]$Version = '1.2.0',
    [string]$Runtime = 'win-x64',
    [switch]$FrameworkDependent,
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
$releaseExe = Join-Path $distRoot "$name.exe"
$hashFile = "$releaseExe.sha256"

if (-not (Test-Path $project)) {
    throw "Project not found: $project"
}

# Remove the previous build of this version so no stale files are uploaded.
if (Test-Path $dist) { Remove-Item $dist -Recurse -Force }
foreach ($old in @($releaseExe, $hashFile, (Join-Path $distRoot "$name.zip"), (Join-Path $distRoot "$name.zip.sha256"))) {
    if (Test-Path $old) { Remove-Item $old -Force }
}
New-Item -ItemType Directory -Path $dist -Force | Out-Null

$publishArgs = @(
    'publish', $project,
    '-c', 'Release',
    '-r', $Runtime,
    '--self-contained', $(if ($FrameworkDependent) { 'false' } else { 'true' }),
    "-p:Version=$Version",
    '-p:PublishSingleFile=true',
    '-p:IncludeNativeLibrariesForSelfExtract=true',
    '-p:EnableCompressionInSingleFile=true',
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

$builtExe = Join-Path $dist 'AetherLauncher.exe'
if (-not (Test-Path $builtExe)) { throw "The output does not contain AetherLauncher.exe." }

$extra = @(Get-ChildItem $dist -File | Where-Object { $_.Name -ne 'AetherLauncher.exe' })
if ($extra.Count -gt 0) {
    throw "The release is not a single file. Extra files: $($extra.Name -join ', ')"
}

Copy-Item $builtExe $releaseExe -Force

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
        & $tool sign /fd SHA256 /f $cert /p $plain /tr $TimestampUrl /td SHA256 $releaseExe
        if ($LASTEXITCODE -ne 0) { throw "signtool sign failed with exit code $LASTEXITCODE." }

        & $tool verify /pa $releaseExe
        if ($LASTEXITCODE -ne 0) { throw "The signature could not be verified." }

        $signed = $true
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

# ---------------------------------------------------------------- Checksum
$hash = (Get-FileHash $releaseExe -Algorithm SHA256).Hash.ToLowerInvariant()
# LF instead of CRLF so sha256sum -c can verify the file.
[IO.File]::WriteAllText($hashFile, "$hash  $([IO.Path]::GetFileName($releaseExe))`n", [Text.Encoding]::ASCII)

$size = [math]::Round((Get-Item $releaseExe).Length / 1MB, 1)

Write-Host ''
Write-Host "Done: $releaseExe ($size MB)" -ForegroundColor Green
Write-Host "SHA-256: $hash"
Write-Host "Signed: $(if ($signed) { 'yes' } else { 'no (-CertificatePath not set)' })"
Write-Host ''
Write-Host 'Verify on another machine:'
Write-Host "  certutil -hashfile $([IO.Path]::GetFileName($releaseExe)) SHA256"
Write-Host "  Get-AuthenticodeSignature .\$([IO.Path]::GetFileName($releaseExe))"
