param(
    [string]$Workspace = (Split-Path -Parent $PSScriptRoot),
    [string]$RuntimeVersion = '8.0.26',
    [switch]$SkipOfflinePackGeneration
)

$ErrorActionPreference = 'Stop'
$project = Join-Path $Workspace 'src\Qingdan\Qingdan.csproj'
$publishDir = Join-Path $Workspace 'publish\portable'
$dist = Join-Path $Workspace 'dist'
$offlineRoot = Join-Path $Workspace '.offline-runtimes'
$feedDir = Join-Path $offlineRoot 'feed'
$offlineConfig = Join-Path $offlineRoot 'NuGet.offline.config'
$netcorePack = Join-Path $feedDir "Microsoft.NETCore.App.Runtime.win-x64.$RuntimeVersion.nupkg"
$desktopPack = Join-Path $feedDir "Microsoft.WindowsDesktop.App.Runtime.win-x64.$RuntimeVersion.nupkg"

if (-not $SkipOfflinePackGeneration -and (-not (Test-Path $netcorePack) -or -not (Test-Path $desktopPack))) {
    Write-Host 'Preparing offline .NET runtime packages...' -ForegroundColor Cyan
    & (Join-Path $PSScriptRoot 'make-offline-runtime-packs.ps1') -RuntimeVersion $RuntimeVersion -Workspace $Workspace
}

if (-not (Test-Path $offlineConfig)) {
    throw 'Missing offline NuGet configuration. Run build\make-offline-runtime-packs.ps1 first.'
}

$env:DOTNET_CLI_HOME = Join-Path $Workspace '.dotnet'
$env:NUGET_PACKAGES = Join-Path $Workspace '.nuget-offline'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

Write-Host 'Publishing self-contained single-file build...' -ForegroundColor Cyan
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

dotnet publish $project `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:EnableSingleFileAnalyzer=false `
    -p:DebugType=None `
    -p:DebugSymbols=false `
    -p:RestoreIgnoreFailedSources=true `
    --configfile $offlineConfig `
    -o $publishDir

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$portableExe = Join-Path $publishDir 'Qingdan.exe'
if (-not (Test-Path $portableExe)) {
    throw "Published executable not found: $portableExe"
}

if (Test-Path $dist) {
    Remove-Item $dist -Recurse -Force
}
New-Item -ItemType Directory -Path $dist | Out-Null

$distPortable = Join-Path $dist 'Qingdan.exe'
$distInstaller = Join-Path $dist 'QingdanSetup.exe'
Copy-Item $portableExe $distPortable
Copy-Item $portableExe $distInstaller
Copy-Item (Join-Path $Workspace 'README.md') (Join-Path $dist 'README.md')

$zipPath = Join-Path $dist 'Qingdan-Portable.zip'
$zipStage = Join-Path $Workspace '.zip-stage'
if (Test-Path $zipStage) { Remove-Item $zipStage -Recurse -Force }
New-Item -ItemType Directory -Path $zipStage | Out-Null
Copy-Item $portableExe (Join-Path $zipStage 'Qingdan.exe')
Copy-Item (Join-Path $Workspace 'README.md') (Join-Path $zipStage 'README.md')
Compress-Archive -Path (Join-Path $zipStage '*') -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item $zipStage -Recurse -Force

$hashes = Get-ChildItem $dist -File | Where-Object { $_.Name -ne 'SHA256SUMS.txt' } | Sort-Object Name | ForEach-Object {
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($_.Name)"
}
$hashes | Set-Content (Join-Path $dist 'SHA256SUMS.txt') -Encoding utf8

Write-Host ''
Write-Host 'Build complete:' -ForegroundColor Green
Get-ChildItem $dist -File | Select-Object Name, @{Name = 'SizeMB'; Expression = { [math]::Round($_.Length / 1MB, 1) } } | Format-Table -AutoSize
