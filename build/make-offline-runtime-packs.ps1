param(
    [string]$RuntimeVersion = '8.0.26',
    [string]$DotnetRoot = 'C:\Program Files\dotnet',
    [string]$Workspace = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$outRoot = Join-Path $Workspace '.offline-runtimes'
$feedDir = Join-Path $outRoot 'feed'
$stageRoot = Join-Path $outRoot 'stage'
Remove-Item $outRoot -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $feedDir, $stageRoot -Force | Out-Null

function Get-FileVersion([string]$path) {
    try {
        $version = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($path).FileVersion
        if ([string]::IsNullOrWhiteSpace($version)) { return '0.0.0.0' }
        return $version
    } catch {
        return '0.0.0.0'
    }
}

function Write-RuntimeListEntry(
    [System.Xml.XmlWriter]$writer,
    [string]$type,
    [string]$relativePath,
    [string]$fullPath,
    [string]$assemblyName,
    [string]$publicKeyToken,
    [string]$assemblyVersion,
    [string]$culture,
    [string]$profile,
    [bool]$dropFromSingleFile) {

    $writer.WriteStartElement('File')
    $writer.WriteAttributeString('Type', $type)
    $writer.WriteAttributeString('Path', $relativePath)
    if ($type -eq 'Resources' -and $culture) {
        $writer.WriteAttributeString('Culture', $culture)
    }
    if ($assemblyName) {
        $writer.WriteAttributeString('AssemblyName', $assemblyName)
    }
    if ($publicKeyToken) {
        $writer.WriteAttributeString('PublicKeyToken', $publicKeyToken)
    }
    if ($assemblyVersion) {
        $writer.WriteAttributeString('AssemblyVersion', $assemblyVersion)
    }
    $writer.WriteAttributeString('FileVersion', (Get-FileVersion $fullPath))
    if ($profile) {
        $writer.WriteAttributeString('Profile', $profile)
    }
    if ($dropFromSingleFile) {
        $writer.WriteAttributeString('DropFromSingleFile', 'true')
    }
    $writer.WriteEndElement()
}

function New-RuntimePack {
    param(
        [string]$Id,
        [string]$SharedFrameworkDir,
        [string]$FrameworkName,
        [string]$PackageVersion,
        [string]$TargetVersion,
        [bool]$IncludeWindowsProfiles,
        [string[]]$ExtraNativeFiles
    )

    $stage = Join-Path $stageRoot $Id
    $libDir = Join-Path $stage "runtimes\win-x64\lib\net$TargetVersion"
    $nativeDir = Join-Path $stage 'runtimes\win-x64\native'
    $dataDir = Join-Path $stage 'data'
    New-Item -ItemType Directory -Path $libDir, $nativeDir, $dataDir -Force | Out-Null

    $runtimeListPath = Join-Path $dataDir 'RuntimeList.xml'
    $xmlSettings = New-Object System.Xml.XmlWriterSettings
    $xmlSettings.Indent = $true
    $xmlSettings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $writer = [System.Xml.XmlWriter]::Create($runtimeListPath, $xmlSettings)
    $writer.WriteStartDocument()
    $writer.WriteStartElement('FileList')
    $writer.WriteAttributeString('TargetFrameworkIdentifier', '.NETCoreApp')
    $writer.WriteAttributeString('TargetFrameworkVersion', $TargetVersion)
    $writer.WriteAttributeString('FrameworkName', $FrameworkName)
    $writer.WriteAttributeString('Name', $FrameworkName)
    $writer.WriteRaw("`r`n")

    $managedCount = 0
    $nativeCount = 0
    foreach ($file in Get-ChildItem -Path $SharedFrameworkDir -Recurse -File) {
        $relative = $file.FullName.Substring($SharedFrameworkDir.Length).TrimStart('\')
        $extension = $file.Extension.ToLowerInvariant()

        if ($extension -eq '.json') {
            if ($file.Name -like '*.deps.json' -or $file.Name -like '*.runtimeconfig.json') {
                Copy-Item $file.FullName (Join-Path $libDir $file.Name) -Force
            }
            continue
        }

        if ($extension -notin @('.dll', '.exe')) {
            continue
        }

        $isManaged = $false
        $assemblyName = $null
        $publicKeyToken = $null
        $assemblyVersion = $null
        try {
            $assembly = [System.Reflection.AssemblyName]::GetAssemblyName($file.FullName)
            $isManaged = $true
            $assemblyName = $assembly.Name
            $assemblyVersion = $assembly.Version.ToString()
            $tokenBytes = $assembly.GetPublicKeyToken()
            if ($tokenBytes -and $tokenBytes.Length -gt 0) {
                $publicKeyToken = ([System.BitConverter]::ToString($tokenBytes)).Replace('-', '').ToLowerInvariant()
            }
        } catch {
            $isManaged = $false
        }

        if ($isManaged) {
            $destination = Join-Path $libDir $relative
            $destinationParent = Split-Path -Parent $destination
            New-Item -ItemType Directory -Path $destinationParent -Force | Out-Null
            Copy-Item $file.FullName $destination -Force

            $culture = ''
            $parentRelative = Split-Path -Parent $relative
            if ($parentRelative) {
                $culture = Split-Path -Leaf $parentRelative
            }

            $type = if ($culture) { 'Resources' } else { 'Managed' }
            $profile = if ($IncludeWindowsProfiles) { 'WindowsForms;WPF' } else { '' }
            $assetPath = "runtimes/win-x64/lib/net$TargetVersion/" + ($relative -replace '\\', '/')
            Write-RuntimeListEntry -writer $writer -type $type -relativePath $assetPath -fullPath $file.FullName `
                -assemblyName $assemblyName -publicKeyToken $publicKeyToken -assemblyVersion $assemblyVersion `
                -culture $culture -profile $profile -dropFromSingleFile $false
            $managedCount++
        }
        else {
            $destination = Join-Path $nativeDir $file.Name
            Copy-Item $file.FullName $destination -Force
            $profile = if ($IncludeWindowsProfiles) { 'WPF' } else { '' }
            $assetPath = "runtimes/win-x64/native/$($file.Name)"
            Write-RuntimeListEntry -writer $writer -type 'Native' -relativePath $assetPath -fullPath $file.FullName `
                -assemblyName '' -publicKeyToken '' -assemblyVersion '' -culture '' -profile $profile `
                -dropFromSingleFile (-not $IncludeWindowsProfiles)
            $nativeCount++
        }
    }

    foreach ($extra in $ExtraNativeFiles) {
        if (-not (Test-Path $extra)) { continue }
        $name = Split-Path -Leaf $extra
        if (Test-Path (Join-Path $nativeDir $name)) { continue }
        Copy-Item $extra (Join-Path $nativeDir $name) -Force
        $assetPath = "runtimes/win-x64/native/$name"
        Write-RuntimeListEntry -writer $writer -type 'Native' -relativePath $assetPath -fullPath $extra `
            -assemblyName '' -publicKeyToken '' -assemblyVersion '' -culture '' -profile '' `
            -dropFromSingleFile $true
        $nativeCount++
    }

    $writer.WriteEndElement()
    $writer.WriteEndDocument()
    $writer.Close()

    $nuspec = @"
<?xml version="1.0" encoding="utf-8"?>
<package xmlns="http://schemas.microsoft.com/packaging/2011/08/nuspec.xsd">
  <metadata>
    <id>$Id</id>
    <version>$PackageVersion</version>
    <authors>Microsoft</authors>
    <license type="expression">MIT</license>
    <projectUrl>https://dot.net/</projectUrl>
    <description>Internal implementation package not meant for direct consumption. Please do not reference directly.</description>
    <copyright>© Microsoft Corporation. All rights reserved.</copyright>
    <serviceable>true</serviceable>
    <packageTypes>
      <packageType name="DotnetPlatform" />
    </packageTypes>
  </metadata>
</package>
"@
    Set-Content -Path (Join-Path $stage "$Id.nuspec") -Value $nuspec -Encoding utf8

    $contentTypes = @"
<?xml version="1.0" encoding="utf-8"?>
<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
  <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
  <Default Extension="psmdcp" ContentType="application/vnd.openxmlformats-package.core-properties+xml" />
  <Default Extension="nuspec" ContentType="application/octet-stream" />
  <Default Extension="dll" ContentType="application/octet-stream" />
  <Default Extension="exe" ContentType="application/octet-stream" />
  <Default Extension="json" ContentType="application/octet-stream" />
  <Default Extension="xml" ContentType="application/octet-stream" />
  <Default Extension="txt" ContentType="application/octet-stream" />
</Types>
"@
    Set-Content -LiteralPath (Join-Path $stage '[Content_Types].xml') -Value $contentTypes -Encoding utf8

    $nupkgPath = Join-Path $feedDir "$Id.$PackageVersion.nupkg"
    if (Test-Path $nupkgPath) { Remove-Item $nupkgPath -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($stage, $nupkgPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    Write-Output "Packed $Id $PackageVersion : managed=$managedCount native=$nativeCount size=$([math]::Round((Get-Item $nupkgPath).Length / 1MB, 1)) MB"
}

$netcoreDir = Join-Path $DotnetRoot "shared\Microsoft.NETCore.App\$RuntimeVersion"
$wpfDir = Join-Path $DotnetRoot "shared\Microsoft.WindowsDesktop.App\$RuntimeVersion"
$hostfxr = Join-Path $DotnetRoot "host\fxr\$RuntimeVersion\hostfxr.dll"

if (-not (Test-Path $netcoreDir)) { throw "Missing runtime: $netcoreDir" }
if (-not (Test-Path $wpfDir)) { throw "Missing desktop runtime: $wpfDir" }

New-RuntimePack -Id 'Microsoft.NETCore.App.Runtime.win-x64' -SharedFrameworkDir $netcoreDir `
    -FrameworkName 'Microsoft.NETCore.App' -PackageVersion $RuntimeVersion -TargetVersion '8.0' `
    -IncludeWindowsProfiles $false -ExtraNativeFiles @($hostfxr)

New-RuntimePack -Id 'Microsoft.WindowsDesktop.App.Runtime.win-x64' -SharedFrameworkDir $wpfDir `
    -FrameworkName 'Microsoft.WindowsDesktop.App' -PackageVersion $RuntimeVersion -TargetVersion '8.0' `
    -IncludeWindowsProfiles $true -ExtraNativeFiles @()

$aspnetDir = Join-Path $DotnetRoot "shared\Microsoft.AspNetCore.App\$RuntimeVersion"
if (Test-Path $aspnetDir) {
    New-RuntimePack -Id 'Microsoft.AspNetCore.App.Runtime.win-x64' -SharedFrameworkDir $aspnetDir `
        -FrameworkName 'Microsoft.AspNetCore.App' -PackageVersion $RuntimeVersion -TargetVersion '8.0' `
        -IncludeWindowsProfiles $false -ExtraNativeFiles @()
}

$config = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="offline" value="$feedDir" />
  </packageSources>
</configuration>
"@
Set-Content -Path (Join-Path $outRoot 'NuGet.offline.config') -Value $config -Encoding utf8
Write-Output "Offline feed ready: $feedDir"
