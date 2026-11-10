[CmdletBinding()]
param(
    [string]$DotNet = 'dotnet',
    [string]$Wix,
    [switch]$SkipInstaller
)

$ErrorActionPreference = 'Stop'
$repository = Split-Path -Parent $PSScriptRoot
$version = ([xml](Get-Content -Raw -LiteralPath (Join-Path $repository 'Directory.Build.props'))).Project.PropertyGroup.Version
$releaseDirectory = Join-Path $repository 'artifacts'
$publishDirectory = Join-Path $releaseDirectory "EduCast-Studio-$version-win-x64"
$project = Join-Path $repository 'apps/desktop/EduCast.Studio.csproj'
$archive = $publishDirectory + '.zip'

New-Item -ItemType Directory -Path $publishDirectory -Force | Out-Null
$msbuildPublishDirectory = $publishDirectory.Replace('%', '%25').Replace(',', '%2C').Replace(';', '%3B')
& $DotNet publish $project -c Release -r win-x64 --no-self-contained "-p:PublishDir=$msbuildPublishDirectory" -p:DebugType=None -m:1 -nr:false
if ($LASTEXITCODE -ne 0) {
    throw 'The desktop publish failed.'
}

$releaseFiles = @(
    'EduCast.Studio.exe',
    'EduCast.Studio.dll',
    'EduCast.Studio.deps.json',
    'EduCast.Studio.runtimeconfig.json',
    'EduCast.Classroom.dll',
    'Microsoft.Web.WebView2.Core.dll',
    'Microsoft.Web.WebView2.WinForms.dll',
    'Microsoft.Web.WebView2.Wpf.dll',
    'Microsoft.Windows.SDK.NET.dll',
    'WebView2Loader.dll',
    'WinRT.Runtime.dll'
) | ForEach-Object { Join-Path $publishDirectory $_ }
Compress-Archive -LiteralPath $releaseFiles -DestinationPath $archive -Force
$packages = @($archive)

if (-not $SkipInstaller) {
    if (-not $Wix) {
        $Wix = (Get-Command wix -ErrorAction Stop).Source
    }
    $installer = $publishDirectory + '.msi'
    $authoring = Join-Path $repository 'installer/EduCast.wxs'
    & $Wix build $authoring -arch x64 -wx -d "PublishDirectory=$publishDirectory" -d "Version=$version" -out $installer
    if ($LASTEXITCODE -ne 0) {
        throw 'The MSI build failed.'
    }
    $packages += $installer
}

$hashes = Get-FileHash -LiteralPath $packages -Algorithm SHA256
$hashes | ForEach-Object {
    $_.Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $_.Path)
} | Set-Content -LiteralPath (Join-Path $releaseDirectory 'SHA256SUMS.txt') -Encoding utf8
$hashes

if (-not $SkipInstaller) {
    & (Join-Path $PSScriptRoot 'Remove-OldBuilds.ps1') -KeepVersion ([version]$version)
}
