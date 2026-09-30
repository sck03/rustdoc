$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/native-docker-lifecycle.ps1')

function Assert-Contract([bool]$Condition, [string]$Message) {
    if (-not $Condition) { throw $Message }
}

# Model a running API holding PostgreSQL's instance lock. Exercise the actual
# orchestration with failures at every external boundary, without a Docker daemon.
function Invoke-ExportDocExternal {
    param($FilePath, [string[]]$Arguments, $Environment, $WorkingDirectory, $TimeoutSeconds, $DisplayName)
    Assert-Contract ($FilePath -eq 'docker-fixture' -and $WorkingDirectory -eq $PSScriptRoot) 'Lost process context.'
    Assert-Contract ($Environment.NATIVE_IMAGE -eq 'example/app:fixed') 'Lost selected image.'
    Assert-Contract ($TimeoutSeconds -gt 0 -and $DisplayName.Length -gt 0) 'Missing timeout or operation label.'
    Assert-Contract (($Arguments[0..2] -join ' ') -eq 'compose --file fixture.yml') 'Lost Compose selection.'
    $command = $Arguments[3..($Arguments.Count - 1)]
    $script:calls.Add($command -join ' ')
    if ($script:calls.Count -eq $script:failAt) { throw 'Injected Docker failure.' }
    $service = $command[-1]
    switch ($command[0]) {
        'build' { Assert-Contract ($service -eq 'application' -and $script:active) 'Build interrupted the API.' }
        'stop' {
            Assert-Contract ($service -eq 'application') 'Only stop API before maintenance.'
            $script:active = $false
        }
        'down' {
            Assert-Contract ($command.Count -eq 1) 'Stopping must retain volumes.'
            $script:active = $false
        }
        '--profile' {
            Assert-Contract (-not $script:active -and $script:databaseReady) 'Maintenance conflicts with API lock or unready database.'
            Assert-Contract ($service -eq $script:expectedMaintenance) 'Wrong maintenance operation.'
            foreach ($flag in @('maintenance', 'run', '--rm', '--no-deps', '--no-build')) {
                Assert-Contract ($command -contains $flag) "Missing maintenance boundary: $flag"
            }
            $script:maintained = $true
        }
        'up' {
            Assert-Contract ($command -contains '--wait') 'Must wait for readiness.'
            if ($service -eq 'postgres') {
                Assert-Contract (-not $script:active) 'Database update must follow API stop.'
                $script:databaseReady = $true
            } else {
                Assert-Contract ($service -eq 'application' -and $script:maintained) 'API started before maintenance succeeded.'
                Assert-Contract ($command -contains '--no-deps' -and $command -contains '--no-build') 'API restart can repeat maintenance/build.'
                $script:active = $true
            }
        }
        default { throw 'Unexpected Compose command.' }
    }
}

$parameters = @{ Docker = 'docker-fixture'; ComposeArguments = @('compose', '--file', 'fixture.yml');
    Environment = @{ NATIVE_IMAGE = 'example/app:fixed' }; WorkingDirectory = $PSScriptRoot }
$scenarios = @(
    @{ Options = @{}; Steps = 5; Maintenance = 'initialize' },
    @{ Options = @{ SkipBuild = $true }; Steps = 4; Maintenance = 'initialize' },
    @{ Options = @{ RestorePending = $true }; Steps = 4; Maintenance = 'restore' },
    @{ Options = @{ Stop = $true }; Steps = 1; Maintenance = '' }
)
$checks = 0
foreach ($scenario in $scenarios) {
    $options = $scenario.Options
    for ($failure = 0; $failure -le $scenario.Steps; $failure++) {
        $script:calls = [System.Collections.Generic.List[string]]::new()
        $script:failAt = $failure
        $script:active = $true
        $script:databaseReady = $false
        $script:maintained = $false
        $script:expectedMaintenance = $scenario.Maintenance
        $caught = $false
        try { Invoke-ExportDocDockerLifecycle @parameters @options } catch {
            if ($_.Exception.Message -ne 'Injected Docker failure.') { throw }
            $caught = $true
        }
        Assert-Contract ($caught -eq ($failure -gt 0)) 'External failure did not propagate.'
        Assert-Contract ($script:calls.Count -eq $(if ($failure) { $failure } else { $scenario.Steps })) 'Continued after failure or skipped a lifecycle phase.'
        if ($failure -gt 0 -and $script:calls[-1] -match '^(up|--profile)') {
            Assert-Contract (-not $script:active) 'Failure restarted the API.'
        }
        if ($failure -eq 0 -and -not $options.Stop) {
            Assert-Contract $script:active 'API did not restart.'
            $script:maintained = $false
            Invoke-ExportDocDockerLifecycle @parameters @options
            Assert-Contract ($script:maintained -and $script:active) 'Repeated invocation skipped fresh maintenance.'
        }
        $checks++
    }
}
Write-Host "Docker lifecycle contracts passed: $checks startup, restart, restore, stop and failure scenarios."
