<#
    Baut die Ausgabe, die heruntergeladen wird:

      dist/AetherLauncher-<Version>-<Runtime>/AetherLauncher.exe   eine Datei, kein Installer
      dist/AetherLauncher-<Version>-<Runtime>.zip                  zum Hochladen
      dist/AetherLauncher-<Version>-<Runtime>.zip.sha256           Prüfsumme

    Was der Build für den Schutz tut:
      * keine .pdb und keine XML-Dokumentation der Fremdbibliotheken im Download -
        keine Klassennamen, Zeilennummern oder Kommentare für neugierige Blicke
      * Single-File und ReadyToRun (IL bleibt enthalten, startet aber schneller)
      * SHA-256 neben dem Archiv, damit ein Download prüfbar ist
      * mit -CertificatePath zusätzlich eine echte Authenticode-Signatur (signtool):
        Windows und der Nutzer sehen, dass die Datei von dir kommt und nicht verändert wurde

    Wichtig, ehrlich: keine dieser Stufen macht eine .NET-Anwendung "unmöglich" zu
    analysieren. Verschlüsselung schützt die gespeicherten Schlüssel, nicht den Code;
    wer den Prozess unter demselben Windows-Benutzer debuggt, sieht entschlüsselte Werte.
    Deshalb liegt in Aether nichts Geheimes im Programm: kein Passwort, kein API-Geheimnis.
    Die Kontoschlüssel sind mit AES-256-GCM verschlüsselt, der AES-Schlüssel ist per DPAPI
    an den Windows-Benutzer und eine app-eigene Entropie gebunden (siehe Dpapi.cs).

    Aufrufe:
      powershell -NoProfile -ExecutionPolicy Bypass -File tools/build-release.ps1
      ... -Version 1.2.0
      ... -SelfContained                      (bringt die .NET-Runtime mit, ~70 MB)
      ... -CertificatePath .\aether.pfx       (fragt das Passwort ab)
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
    throw "Projekt nicht gefunden: $project"
}

# Alte Ausgabe derselben Version wegwerfen, damit keine Reste mit hochgeladen werden.
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

Write-Host "Baue $name ..." -ForegroundColor Cyan
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish ist mit $LASTEXITCODE fehlgeschlagen." }

# Falls das SDK trotz der Schalter Symbole oder Doku mitkopiert: raus damit.
Get-ChildItem $dist -Filter *.pdb -File -ErrorAction SilentlyContinue | Remove-Item -Force
Get-ChildItem $dist -Filter *.xml -File -ErrorAction SilentlyContinue | Remove-Item -Force

$exe = Join-Path $dist 'AetherLauncher.exe'
if (-not (Test-Path $exe)) { throw "Die Ausgabe enthält keine AetherLauncher.exe." }

# ---------------------------------------------------------------- Signieren (optional)
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
        throw "signtool.exe wurde nicht gefunden. Installiere das Windows SDK oder signiere die Datei auf dem Build-Rechner."
    }

    if (-not $CertificatePassword) {
        $CertificatePassword = Read-Host -Prompt "Passwort für $([IO.Path]::GetFileName($cert))" -AsSecureString
    }

    $bstr = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($CertificatePassword)
    try {
        $plain = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($bstr)
        $tool = if ($signtool -is [System.Management.Automation.ApplicationInfo]) { $signtool.Source } else { $signtool.FullName }
        & $tool sign /fd SHA256 /f $cert /p $plain /tr $TimestampUrl /td SHA256 $exe
        if ($LASTEXITCODE -ne 0) { throw "signtool sign ist mit $LASTEXITCODE fehlgeschlagen." }

        & $tool verify /pa $exe
        if ($LASTEXITCODE -ne 0) { throw "Die Signatur konnte nicht bestätigt werden." }

        $signed = $true
    }
    finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($bstr)
    }
}

# ---------------------------------------------------------------- Packen und prüfbar machen
Compress-Archive -Path (Join-Path $dist '*') -DestinationPath $zip -CompressionLevel Optimal -Force

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$hashFile = "$zip.sha256"
# LF statt CRLF: so lässt sich die Datei auch mit sha256sum -c prüfen.
[IO.File]::WriteAllText($hashFile, "$hash  $([IO.Path]::GetFileName($zip))`n", [Text.Encoding]::ASCII)

$size = [math]::Round((Get-Item $zip).Length / 1MB, 1)

Write-Host ''
Write-Host "Fertig: $zip ($size MB)" -ForegroundColor Green
Write-Host "SHA-256: $hash"
Write-Host "Signiert: $(if ($signed) { 'ja' } else { 'nein (ohne -CertificatePath)' })"
Write-Host ''
Write-Host 'Zum Prüfen auf einem anderen Rechner:'
Write-Host "  certutil -hashfile $([IO.Path]::GetFileName($zip)) SHA256"
Write-Host "  Get-AuthenticodeSignature .\AetherLauncher.exe"
