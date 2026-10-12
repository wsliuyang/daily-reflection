param(
    [switch]$Verify,
    [string]$OutputName = '每日一记吧.exe',
    [string]$OutputDirectory = 'bin'
)

$ErrorActionPreference = 'Stop'
$appRoot = $PSScriptRoot
if ([System.IO.Path]::GetFileName($OutputName) -ne $OutputName -or
    -not $OutputName.EndsWith('.exe', [System.StringComparison]::OrdinalIgnoreCase)) {
    throw 'OutputName 必须是 .exe 文件名。'
}
if (-not [System.IO.Path]::IsPathRooted($OutputDirectory)) {
    $OutputDirectory = Join-Path $appRoot $OutputDirectory
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$configurationSource = Join-Path $appRoot 'src\App.config'
$frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
if (-not (Test-Path -LiteralPath (Join-Path $frameworkPath 'csc.exe'))) {
    $frameworkPath = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319'
}
$compilerPath = Join-Path $frameworkPath 'csc.exe'
$wpfPath = Join-Path $frameworkPath 'WPF'
foreach ($requiredFile in @($configurationSource, $compilerPath, (Join-Path $wpfPath 'PresentationFramework.dll'))) {
    if (-not (Test-Path -LiteralPath $requiredFile -PathType Leaf)) {
        throw ('缺少构建文件：' + $requiredFile + '。请在装有 .NET Framework 4.7.2 或更高版本的 Windows 上构建。')
    }
}
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$appExecutable = Join-Path $OutputDirectory $OutputName
$arguments = @('/nologo', '/target:winexe', '/platform:anycpu', '/optimize+', '/utf8output', '/langversion:5',
    ('/out:' + $appExecutable),
    ('/win32manifest:' + (Join-Path $appRoot 'src\app.manifest')),
    ('/win32icon:' + (Join-Path $appRoot 'src\app.ico')),
    ('/reference:' + (Join-Path $frameworkPath 'System.Web.Extensions.dll')),
    ('/reference:' + (Join-Path $wpfPath 'WindowsBase.dll')),
    ('/reference:' + (Join-Path $wpfPath 'PresentationCore.dll')),
    ('/reference:' + (Join-Path $wpfPath 'PresentationFramework.dll')),
    ('/reference:' + (Join-Path $frameworkPath 'System.Xaml.dll')))
foreach ($resource in @('SharedStyles', 'MainWindow', 'HistoryWindow')) {
    $arguments += '/resource:' + (Join-Path $appRoot ('src\' + $resource + '.xaml')) + ',DailyReflection.' + $resource + '.xaml'
}
$arguments += @(Get-ChildItem -LiteralPath (Join-Path $appRoot 'src') -Filter '*.cs' | ForEach-Object { $_.FullName })
& $compilerPath @arguments
if ($LASTEXITCODE -ne 0) { throw '编译失败。' }
Copy-Item -LiteralPath $configurationSource -Destination ($appExecutable + '.config') -Force
Write-Output ('已生成：' + $appExecutable)

if ($Verify) {
    $testDirectory = Join-Path $appRoot ('artifacts\verification-' + [Guid]::NewGuid().ToString('N'))
    $testProcess = Start-Process -FilePath $appExecutable -ArgumentList @('--self-test', ('"' + $testDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($testProcess.ExitCode -ne 0) {
        $failurePath = Join-Path $testDirectory 'failure.txt'
        if (Test-Path -LiteralPath $failurePath) { Get-Content -LiteralPath $failurePath -Raw -Encoding UTF8 }
        throw ('验证失败，输出目录：' + $testDirectory)
    }
    $reportPath = Join-Path $testDirectory 'test-results.txt'
    if (-not (Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw '验证进程未生成报告。' }
    Copy-Item -LiteralPath $reportPath -Destination (Join-Path $appRoot 'artifacts\test-results.txt') -Force
    Get-Content -LiteralPath $reportPath -Encoding UTF8
}
