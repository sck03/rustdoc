[CmdletBinding()]
param(
    [string]$RuntimeRoot,
    [ValidateRange(1024, 65535)][int]$Port = 5188,
    [string]$BindAddress = '127.0.0.1',
    [string]$Image = 'exportdoc-rust-native:local',
    [switch]$SkipBuild,
    [switch]$PrepareOnly,
    [switch]$Stop,
    [switch]$RestorePending,
    [switch]$NoPause
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'lib/build-script-support.ps1')
$interactiveLaunch = Test-ExportDocPauseEnabled -NoPauseRequested $NoPause
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 1
    exit 1
}
$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ($Image -notmatch '\A[a-z0-9][a-z0-9./:_@-]*\z') { throw 'Invalid container image reference.' }
if ($Image -ne 'exportdoc-rust-native:local' -and -not $SkipBuild -and -not $Stop -and -not $PrepareOnly -and -not $RestorePending) { throw 'Use -SkipBuild to run a published image.' }
if ([string]::IsNullOrWhiteSpace($RuntimeRoot)) { $RuntimeRoot = Join-Path $repositoryRoot 'deploy/rust-native/runtime' }
$runtimePath = [System.IO.Path]::GetFullPath($RuntimeRoot)
if ($runtimePath -eq [System.IO.Path]::GetPathRoot($runtimePath) -or (Test-ExportDocPathEqual -Left $runtimePath -Right $repositoryRoot)) { throw 'Use a dedicated native runtime directory.' }
$currentPath = $runtimePath
while ($currentPath) {
    if (Test-Path -LiteralPath $currentPath) {
        if ((Get-Item -LiteralPath $currentPath -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Linked runtime paths are not allowed.' }
    }
    $currentPath = [System.IO.Path]::GetDirectoryName($currentPath)
}
$bindIp = $null
if (-not [System.Net.IPAddress]::TryParse($BindAddress, [ref]$bindIp) -or $bindIp.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork) { throw 'BindAddress must be an explicit IPv4 address.' }
if (@($PrepareOnly, $Stop, $RestorePending).Where({ $_ }).Count -gt 1) { throw 'PrepareOnly, Stop and RestorePending cannot be combined.' }
$markerPath = Join-Path $runtimePath 'native-runtime.json'
if (Test-Path -LiteralPath $runtimePath) {
    if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf) -and @(Get-ChildItem -LiteralPath $runtimePath -Force).Count -gt 0) { throw 'Refusing to adopt an unmarked non-empty runtime directory.' }
    if (Test-Path -LiteralPath $markerPath) {
        $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
        if ($marker.schemaVersion -ne 1 -or $marker.purpose -ne 'exportdoc-rust-native-docker') { throw 'The native runtime marker is invalid.' }
    }
}
if ($Stop -and -not (Test-Path -LiteralPath $markerPath)) { throw 'No prepared native Docker runtime exists.' }
if (-not $Stop) {
    New-Item -ItemType Directory -Force -Path $runtimePath | Out-Null
    # The directory is private on the host. Readable files inside it can then be
    # mounted by Compose into only the containers that explicitly receive them.
    if ($IsWindows) {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent().User
        $runtimeDirectory = [System.IO.DirectoryInfo]::new($runtimePath)
        $acl = [System.IO.FileSystemAclExtensions]::GetAccessControl($runtimeDirectory, [System.Security.AccessControl.AccessControlSections]::Access)
        $acl.SetAccessRuleProtection($true, $false)
        foreach ($principal in @($identity, [System.Security.Principal.SecurityIdentifier]::new('S-1-5-18'))) {
            $rule = [System.Security.AccessControl.FileSystemAccessRule]::new($principal, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow')
            $acl.SetAccessRule($rule)
        }
        [System.IO.FileSystemAclExtensions]::SetAccessControl($runtimeDirectory, $acl)
    } else {
        [System.IO.File]::SetUnixFileMode($runtimePath, [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor [System.IO.UnixFileMode]::UserExecute)
    }
    if (-not (Test-Path -LiteralPath $markerPath)) {
        @{ schemaVersion = 1; purpose = 'exportdoc-rust-native-docker' } | ConvertTo-Json | Set-Content -LiteralPath $markerPath -Encoding utf8
    }
    function Write-NativeSecret {
        param([string]$Name, [string]$Value)
        $path = Join-Path $runtimePath $Name
        if (Test-Path -LiteralPath $path) {
            if ((Get-Item -LiteralPath $path -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Linked secret files are not allowed.' }
            if ([System.IO.File]::ReadAllText($path) -ne $Value) { throw "Existing $Name does not match this runtime; it was preserved." }
            return
        }
        $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        try { $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Value); $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
        if (-not $IsWindows) { [System.IO.File]::SetUnixFileMode($path, [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor [System.IO.UnixFileMode]::GroupRead -bor [System.IO.UnixFileMode]::OtherRead) }
    }
    function Get-NativeSecret {
        param([string]$Name)
        $path = Join-Path $runtimePath $Name
        if (Test-Path -LiteralPath $path) {
            if ((Get-Item -LiteralPath $path -Force).Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw 'Linked secret files are not allowed.' }
            $value = [System.IO.File]::ReadAllText($path)
            if ($value -notmatch '^[A-F0-9]{64}$') { throw "Existing $Name is invalid and was preserved." }
            return $value
        }
        $value = [Convert]::ToHexString([System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
        Write-NativeSecret -Name $Name -Value $value
        return $value
    }
    $appPassword = Get-NativeSecret -Name 'app-password.txt'
    $maintenancePassword = Get-NativeSecret -Name 'maintenance-password.txt'
    $postgresBootstrapPassword = Get-NativeSecret -Name 'postgres-bootstrap-password.txt'
    $bootstrap = Get-NativeSecret -Name 'bootstrap-token.txt'
    Write-NativeSecret -Name 'app-connection.txt' -Value "host=postgres port=5432 dbname=exportdoc_native user=exportdoc_app password=$appPassword connect_timeout=10 sslmode=disable"
    Write-NativeSecret -Name 'maintenance-connection.txt' -Value "host=postgres port=5432 dbname=exportdoc_native user=exportdoc_maintenance password=$maintenancePassword connect_timeout=10 sslmode=disable"
    $appPassword = $null
    $maintenancePassword = $null
    $postgresBootstrapPassword = $null
    $bootstrap = $null
}
if ($PrepareOnly) {
    Write-Host "Native Docker configuration prepared: $runtimePath"
} else {
    $docker = (Get-Command docker -ErrorAction Stop).Source
    $environment = @{ NATIVE_RUNTIME_ROOT = $runtimePath.Replace('\', '/'); NATIVE_PORT = "$Port"; NATIVE_BIND_ADDRESS = $BindAddress; NATIVE_IMAGE = $Image }
    $arguments = @('compose', '--project-name', 'exportdoc-rust-native', '--file', (Join-Path $repositoryRoot 'deploy/rust-native/compose.yml'))
    if ($RestorePending) {
        Invoke-ExportDocExternal -FilePath $docker -Arguments ($arguments + @('stop', 'application')) -Environment $environment -WorkingDirectory $repositoryRoot -TimeoutSeconds 240 -DisplayName 'Stop API before database maintenance'
        Invoke-ExportDocExternal -FilePath $docker -Arguments ($arguments + @('--profile', 'maintenance', 'run', '--rm', 'restore')) -Environment $environment -WorkingDirectory $repositoryRoot -TimeoutSeconds 3600 -DisplayName 'Apply staged restore with maintenance credentials'
        Invoke-ExportDocExternal -FilePath $docker -Arguments ($arguments + @('up', '--detach', '--wait', '--wait-timeout', '180', 'application')) -Environment $environment -WorkingDirectory $repositoryRoot -TimeoutSeconds 240 -DisplayName 'Restart restored API'
        Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 0
        return
    }
    $arguments += $(if ($Stop) { @('down') } else { @('up', $(if ($SkipBuild) { '--no-build' } else { '--build' }), '--detach', '--wait', '--wait-timeout', '180') })
    Invoke-ExportDocExternal -FilePath $docker -Arguments $arguments -Environment $environment -WorkingDirectory $repositoryRoot -TimeoutSeconds 3600 -DisplayName 'Rust native Docker application'
    if (-not $Stop) { Write-Host "Native web application: http://${BindAddress}:$Port" }
}
Wait-ExportDocInteractiveExit -Enabled $interactiveLaunch -ExitCode 0
