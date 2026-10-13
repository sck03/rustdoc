[CmdletBinding()]
param(
    [string]$RuntimeRoot,
    [ValidateRange(1024, 65535)][int]$Port = 5188,
    [string]$BindAddress = '127.0.0.1',
    [string]$Image = 'exportdoc-rust-native:local',
    [string]$Version,
    [switch]$SkipBuild,
    [switch]$PrepareOnly,
    [switch]$Stop,
    [switch]$RestorePending,
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
. (Join-Path $PSScriptRoot 'lib/native-docker-lifecycle.ps1')
. (Join-Path $PSScriptRoot 'lib/native-docker-runtime.ps1')
$interactiveLaunch = Test-ExportDocPauseEnabled -NoPauseRequested $NoPause
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 1
    exit 1
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ($Image -notmatch '\A[a-z0-9][a-z0-9./:_@-]*\z') { throw 'Invalid container image reference.' }
if ($Image -ne 'exportdoc-rust-native:local' -and -not $SkipBuild -and -not $Stop -and -not $PrepareOnly -and -not $RestorePending) { throw 'Use -SkipBuild to run a published image.' }
$bindIp = $null
if (-not [System.Net.IPAddress]::TryParse($BindAddress, [ref]$bindIp) -or $bindIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) { throw 'BindAddress must be an explicit IPv4 address.' }
if (@($PrepareOnly, $Stop, $RestorePending).Where({ $_ }).Count -gt 1) { throw 'PrepareOnly, Stop and RestorePending cannot be combined.' }
if ($Version.Length -gt 0) {
    if ($SkipBuild -or $PrepareOnly -or $Stop -or $RestorePending) { throw 'Version requires a source image build; it cannot relabel an existing image.' }
    Initialize-ExportDocProductVersion -RepositoryRoot $repositoryRoot -Version $Version
}
$runtimePath = Initialize-ExportDocDockerRuntime -RuntimeRoot $RuntimeRoot -RepositoryRoot $repositoryRoot -Stop:$Stop -RestorePending:$RestorePending
if ($PrepareOnly) {
    Write-Host "Native Docker configuration prepared: $runtimePath"
} else {
    $docker = (Get-Command docker -ErrorAction Stop).Source
    $environment = @{ NATIVE_RUNTIME_ROOT = $runtimePath.Replace('\', '/'); NATIVE_PORT = "$Port"; NATIVE_BIND_ADDRESS = $BindAddress; NATIVE_IMAGE = $Image }
    $arguments = @('compose', '--project-name', 'exportdoc-rust-native', '--file', (Join-Path $repositoryRoot 'deploy/rust-native/compose.yml'))
    Invoke-ExportDocDockerLifecycle -Docker $docker -ComposeArguments $arguments -Environment $environment `
        -WorkingDirectory $repositoryRoot -SkipBuild:$SkipBuild -Stop:$Stop -RestorePending:$RestorePending
    if (-not $Stop) { Write-Host "Native web application: http://${BindAddress}:$Port" }
}
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 0
