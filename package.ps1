param([string]$OutputDirectory = 'dist')

$ErrorActionPreference = 'Stop'
$appRoot = $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory = Join-Path $appRoot $OutputDirectory }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$stagingRoot = Join-Path $OutputDirectory ('staging-' + [Guid]::NewGuid().ToString('N'))
$buildDirectory = Join-Path $stagingRoot 'build'
& (Join-Path $appRoot 'build.ps1') -Verify -OutputDirectory $buildDirectory

$appExecutable = Join-Path $buildDirectory '每日一记吧.exe'
$fileVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($appExecutable)
$releaseVersion = '{0}.{1}.{2}' -f $fileVersion.FileMajorPart, $fileVersion.FileMinorPart, $fileVersion.FileBuildPart
$releaseName = 'DailyReflection-v' + $releaseVersion + '-windows-portable'
$packageDirectory = Join-Path $stagingRoot $releaseName
New-Item -ItemType Directory -Path $packageDirectory | Out-Null

# Use an explicit file list: never copy a running installation or its journal data.
Copy-Item -LiteralPath $appExecutable -Destination $packageDirectory
Copy-Item -LiteralPath ($appExecutable + '.config') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $appRoot 'docs\使用说明.md') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $appRoot 'LICENSE') -Destination $packageDirectory
Copy-Item -LiteralPath (Join-Path $appRoot 'THIRD-PARTY-NOTICES.txt') -Destination $packageDirectory

$zipPath = Join-Path $OutputDirectory ($releaseName + '.zip')
Compress-Archive -LiteralPath $packageDirectory -DestinationPath $zipPath -Force
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    $expectedNames = @('每日一记吧.exe', '每日一记吧.exe.config', '使用说明.md', 'LICENSE', 'THIRD-PARTY-NOTICES.txt') |
        ForEach-Object { $releaseName + '/' + $_ }
    $actualNames = @($archive.Entries | Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
        ForEach-Object { $_.FullName.Replace('\', '/') })
    if (@(Compare-Object $expectedNames $actualNames).Count -ne 0) { throw '发布包内容不符合文件清单。' }
} finally { $archive.Dispose() }
$checksum = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = $zipPath + '.sha256'
[System.IO.File]::WriteAllText($checksumPath, ($checksum + '  ' + [System.IO.Path]::GetFileName($zipPath) + "`n"), [System.Text.Encoding]::ASCII)

# Resolve and verify both paths before deleting only this invocation's staging folder.
$resolvedOutput = (Resolve-Path -LiteralPath $OutputDirectory).ProviderPath.TrimEnd('\', '/')
$resolvedStaging = (Resolve-Path -LiteralPath $stagingRoot).ProviderPath
if (-not $resolvedStaging.StartsWith($resolvedOutput + [System.IO.Path]::DirectorySeparatorChar, [System.StringComparison]::OrdinalIgnoreCase) -or
    [System.IO.Path]::GetFileName($resolvedStaging) -notmatch '^staging-[0-9a-f]{32}$') {
    throw '临时目录路径检查失败，已保留文件。'
}
Remove-Item -LiteralPath $resolvedStaging -Recurse -Force
Write-Output ('发布包：' + $zipPath)
Write-Output ('校验文件：' + $checksumPath)
