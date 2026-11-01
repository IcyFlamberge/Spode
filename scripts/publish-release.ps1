param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x86",
    [string]$Output = "publish\Spode",
    [string]$ObfuscatorCommand = "",
    [string]$ObfuscatorConfig = "",
    [string]$PfxPath = "",
    [string]$PfxPassword = "",
    [string]$TimestampUrl = "http://timestamp.digicert.com",
    [string]$Signtool = "signtool.exe",
    [switch]$Zip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project = Join-Path $root "src\SporeModManager.App\SporeModManager.App.csproj"
$outputPath = Join-Path $root $Output
$version = (Select-Xml -Path $project -XPath "//Version" | Select-Object -First 1).Node.InnerText
if (-not $version) { $version = "1.0.0" }

Write-Host "Publishing Spode to $outputPath ..."
# Clean local build outputs so embedded resources (logo) and generated files are current.
foreach ($projectDir in @("src\SporeModManager.App", "src\SporeModManager.Core")) {
    foreach ($sub in @("bin", "obj")) {
        $path = Join-Path $root (Join-Path $projectDir $sub)
        if (Test-Path $path) { Remove-Item -Recurse -Force $path -ErrorAction SilentlyContinue }
    }
}
dotnet publish $project `
    -c $Configuration `
    -r $Runtime `
    --self-contained false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:GenerateDocumentationFile=false `
    -p:PublishSingleFile=true `
    -p:SatelliteResourceLanguages=en `
    -o $outputPath

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

# Never ship debug symbols or XML docs.
Get-ChildItem $outputPath -Recurse -File -Include *.pdb, *.xml |
    Where-Object { $_.Name -ne "ModInfo.xml" } |
    Remove-Item -Force

if ($ObfuscatorCommand) {
    if (-not (Test-Path $ObfuscatorCommand)) {
        throw "Obfuscator not found: $ObfuscatorCommand"
    }
    if (-not $ObfuscatorConfig -or -not (Test-Path $ObfuscatorConfig)) {
        throw "Obfuscator config not found: $ObfuscatorConfig"
    }
    Write-Host "Running obfuscator ..."
    & $ObfuscatorCommand $ObfuscatorConfig
    if ($LASTEXITCODE -ne 0) {
        throw "Obfuscator failed with exit code $LASTEXITCODE"
    }
}
else {
    Write-Warning "No obfuscator configured. For a protected release, pass -ObfuscatorCommand and -ObfuscatorConfig. See docs\PROTECTION.md."
}

$signFiles = @(
    (Join-Path $outputPath "Spode.exe")
) | Where-Object { Test-Path $_ }

if ($PfxPath) {
    if (-not (Test-Path $PfxPath)) {
        throw "Signing certificate not found: $PfxPath"
    }
    if (-not (Get-Command $Signtool -ErrorAction SilentlyContinue)) {
        $kitRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
        if (Test-Path $kitRoot) {
            $candidate = Get-ChildItem $kitRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
                Where-Object { $_.FullName -match "\\x64\\signtool\.exe$" } |
                Sort-Object FullName -Descending |
                Select-Object -First 1
            if ($candidate) { $Signtool = $candidate.FullName }
        }
    }
    if (-not (Get-Command $Signtool -ErrorAction SilentlyContinue) -and -not (Test-Path $Signtool)) {
        throw "signtool not found: $Signtool. Install the Windows SDK or pass -Signtool <path>."
    }

    Write-Host "Signing Spode binaries ..."
    $signArgs = @(
        "sign", "/fd", "SHA256",
        "/tr", $TimestampUrl, "/td", "SHA256",
        "/f", $PfxPath
    )
    if ($PfxPassword) { $signArgs += @("/p", $PfxPassword) }
    $signArgs += $signFiles

    & $Signtool @signArgs
    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign failed with exit code $LASTEXITCODE"
    }

    & $Signtool verify /pa /v $signFiles
    if ($LASTEXITCODE -ne 0) {
        throw "signtool verify failed with exit code $LASTEXITCODE"
    }
}
else {
    Write-Warning "No PFX certificate supplied. Release is unsigned. See docs\CODE_SIGNING.md."
}

# One-executable release: write the checksum next to the publish folder.
$exePath = Join-Path $outputPath "Spode.exe"
$exeHash = (Get-FileHash $exePath -Algorithm SHA256).Hash.ToLowerInvariant()
$sumFile = Join-Path (Split-Path $outputPath) "Spode.exe.sha256"
Set-Content -Path $sumFile -Value "$exeHash  Spode.exe" -Encoding UTF8
Write-Host "Wrote $sumFile"

if ($Zip) {
    $zipPath = Join-Path $root "publish\Spode-$version-$Runtime.zip"
    if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory(
        $outputPath,
        $zipPath,
        [System.IO.Compression.CompressionLevel]::Optimal,
        $true)

    $zipHash = (Get-FileHash $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $zipSumPath = "$zipPath.sha256"
    Set-Content -Path $zipSumPath -Value "$zipHash  $([System.IO.Path]::GetFileName($zipPath))" -Encoding UTF8
    Write-Host "Created $zipPath"
    Write-Host "Wrote $zipSumPath"
}
else {
    Write-Host "Release folder created at $outputPath (no ZIP; pass -Zip for a GitHub asset)."
}
