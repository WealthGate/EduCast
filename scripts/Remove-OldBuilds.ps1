[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [version]$KeepVersion
)

$repository = (Resolve-Path -LiteralPath (Split-Path -Parent $PSScriptRoot)).ProviderPath
$expected = Join-Path $repository 'artifacts'
if (-not (Test-Path -LiteralPath $expected -PathType Container)) {
    return
}
$root = Get-Item -LiteralPath $expected
if ($root.LinkType -or $root.FullName -ine $expected) {
    throw 'The release artifacts folder does not match the workspace.'
}

$portableFiles = @(
    'EduCast.Studio.exe', 'EduCast.Studio.dll', 'EduCast.Studio.deps.json',
    'EduCast.Studio.runtimeconfig.json', 'EduCast.Classroom.dll',
    'EduCast.Studio.pdb', 'EduCast.Classroom.pdb'
)
$runningFolders = @(Get-Process -Name 'EduCast.Studio' -ErrorAction SilentlyContinue |
    Where-Object Path | ForEach-Object { Split-Path -Parent $_.Path })

function Remove-GeneratedFile($file, [string]$folder) {
    if ($file.PSIsContainer -or $file.LinkType -or $file.DirectoryName -ine $folder) {
        return
    }
    if ($PSCmdlet.ShouldProcess($file.FullName, 'Remove older EduCast build file')) {
        try { Remove-Item -LiteralPath $file.FullName -Force -ErrorAction Stop }
        catch { Write-Warning "Could not remove the older build file $($file.FullName)." }
    }
}

foreach ($entry in Get-ChildItem -LiteralPath $root.FullName -Force) {
    $allowedFiles = @()
    if ($entry.Name -match '^EduCast-Studio-(?<version>\d+\.\d+\.\d+)-win-x64(?:\.(?<type>msi|zip|wixpdb))?$') {
        $previous = [version]$Matches.version
        if ($previous -ge $KeepVersion -or $entry.PSIsContainer -eq $Matches.ContainsKey('type')) {
            continue
        }
        if (-not $entry.PSIsContainer) {
            Remove-GeneratedFile $entry $root.FullName
            continue
        }
        $allowedFiles = $portableFiles
    }
    elseif ($entry.PSIsContainer -and
        $entry.Name -match '^(?:github-v(?<version>\d+\.\d+\.\d+)|v(?<version>\d+\.\d+\.\d+)-release)$') {
        $previous = [version]$Matches.version
        if ($previous -ge $KeepVersion) {
            continue
        }
        $base = "EduCast-Studio-$previous-win-x64"
        $allowedFiles = @("$base.msi", "$base.zip", 'SHA256SUMS.txt')
    }
    else {
        continue
    }

    if ($entry.LinkType -or $entry.Parent.FullName -ine $root.FullName -or $runningFolders -contains $entry.FullName) {
        continue
    }
    foreach ($file in Get-ChildItem -LiteralPath $entry.FullName -Force) {
        if ($allowedFiles -contains $file.Name) {
            Remove-GeneratedFile $file $entry.FullName
        }
    }

    if (-not (Get-ChildItem -LiteralPath $entry.FullName -Force | Select-Object -First 1) -and
        $PSCmdlet.ShouldProcess($entry.FullName, 'Remove empty older EduCast build folder')) {
        try { [IO.Directory]::Delete($entry.FullName, $false) }
        catch { Write-Warning "Could not remove the older build folder $($entry.FullName)." }
    }
}
