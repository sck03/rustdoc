$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
$repo = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path $repo ('.codex-runtime/docker-runtime-tests-' + [Guid]::NewGuid().ToString('N'))
$runtime = Join-Path $fixture 'runtime'
function Invoke-Runtime([string]$Path, [string]$Mode = '-PrepareOnly', [string]$ExpectedFailure = '') {
    $failure = ''
    try {
        Invoke-ExportDocExternal -FilePath (Resolve-ExportDocPowerShellExecutable) -Arguments @(
            '-NoProfile', '-NonInteractive', '-File', (Join-Path $PSScriptRoot 'run-native-docker.ps1'),
            '-RuntimeRoot', $Path, $Mode, '-NoPause'
        ) -CaptureOutput -TimeoutSeconds 30 | Out-Null
    } catch { $failure = $_.Exception.Message }
    if ($ExpectedFailure) {
        if ($failure -notmatch $ExpectedFailure) { throw "Runtime did not reject the expected invalid state: $failure" }
    } elseif ($failure) { throw $failure }
}
function Assert-OriginalFiles($Original, [string]$Missing = '') {
    foreach ($name in $Original.Keys) {
        $path = Join-Path $runtime $name
        if ($name -eq $Missing) {
            if (Test-Path -LiteralPath $path) { throw "Missing secret was regenerated: $name" }
        } elseif ([IO.File]::ReadAllText($path) -cne $Original[$name]) {
            throw "Existing deployment configuration changed: $name"
        }
    }
}
try {
    Invoke-Runtime -Path $runtime
    $original = @{}
    foreach ($file in Get-ChildItem -LiteralPath $runtime -File) { $original[$file.Name] = [IO.File]::ReadAllText($file.FullName) }
    Invoke-Runtime -Path $runtime
    Assert-OriginalFiles $original
    foreach ($name in $original.Keys | Where-Object { $_ -ne 'native-runtime.json' }) {
        $path = Join-Path $runtime $name
        Remove-Item -LiteralPath $path -Force
        Invoke-Runtime -Path $runtime -ExpectedFailure 'missing'
        Assert-OriginalFiles $original $name
        [IO.File]::WriteAllText($path, $original[$name], [Text.UTF8Encoding]::new($false))
    }
    $secret = Join-Path $runtime 'app-password.txt'
    [IO.File]::WriteAllText($secret, 'invalid')
    Invoke-Runtime -Path $runtime -ExpectedFailure 'invalid'
    [IO.File]::WriteAllText($secret, $original['app-password.txt'], [Text.UTF8Encoding]::new($false))
    Assert-OriginalFiles $original
    $unprepared = Join-Path $fixture 'unprepared'
    Invoke-Runtime -Path $unprepared -Mode '-RestorePending' -ExpectedFailure 'No prepared'
    if (Test-Path -LiteralPath $unprepared) { throw 'Restore created a replacement deployment identity.' }
    Write-Host 'Docker runtime contracts passed: repeat preparation, missing/corrupt credentials, and unprepared restore.'
} finally {
    $resolved = [IO.Path]::GetFullPath($fixture)
    if ([IO.Path]::GetDirectoryName($resolved) -ne (Join-Path $repo '.codex-runtime')) { throw 'Unsafe fixture cleanup path.' }
    if (Test-Path -LiteralPath $resolved) { [IO.Directory]::Delete($resolved, $true) }
}
