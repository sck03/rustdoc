# Compose orchestration only. SQL migrations and restore transactions remain in Rust.
function Invoke-ExportDocDockerLifecycle {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$Docker,
        [Parameter(Mandatory = $true)][string[]]$ComposeArguments,
        [Parameter(Mandatory = $true)][hashtable]$Environment,
        [Parameter(Mandatory = $true)][string]$WorkingDirectory,
        [switch]$SkipBuild,
        [switch]$Stop,
        [switch]$RestorePending
    )
    function Invoke-Compose {
        param([string[]]$Command, [string]$Purpose, [int]$Timeout = 240)
        Invoke-ExportDocExternal -FilePath $Docker -Arguments ($ComposeArguments + $Command) `
            -Environment $Environment -WorkingDirectory $WorkingDirectory -TimeoutSeconds $Timeout -DisplayName $Purpose
    }
    if ($Stop) {
        Invoke-Compose -Command @('down') -Purpose 'Stop containers and retain data volumes'
        return
    }
    # Finish compilation before interrupting a running deployment.
    if (-not $SkipBuild -and -not $RestorePending) {
        Invoke-Compose -Command @('build', 'application') -Purpose 'Build Rust native application image' -Timeout 3600
    }
    Invoke-Compose -Command @('stop', 'application') -Purpose 'Stop API before database maintenance'
    Invoke-Compose -Command @('up', '--detach', '--wait', '--wait-timeout', '180', 'postgres') -Purpose 'Wait for PostgreSQL'
    $maintenance = if ($RestorePending) { 'restore' } else { 'initialize' }
    # Always run a fresh maintenance process, even when the image has not changed.
    # Failures propagate: do not restart the API or remove pending restore markers.
    Invoke-Compose -Command @('--profile', 'maintenance', 'run', '--rm', '--no-deps', $maintenance) `
        -Purpose "Run PostgreSQL $maintenance with maintenance credentials" -Timeout 3600
    Invoke-Compose -Command @('up', '--detach', '--no-deps', '--no-build', '--wait', '--wait-timeout', '180', 'application') `
        -Purpose 'Start API after successful database maintenance'
}
