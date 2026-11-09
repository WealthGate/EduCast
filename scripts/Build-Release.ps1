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
    'EduCast.Classroom.dll'
) | ForEach-Object { Join-Path $publishDirectory $_ }
Compress-Archive -LiteralPath $releaseFiles -DestinationPath $archive -Force
Get-FileHash -LiteralPath $archive -Algorithm SHA256

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
    Get-FileHash -LiteralPath $installer -Algorithm SHA256
}
