# Private Docker deployment identity and secret-file preparation. No lifecycle or SQL work.
function Initialize-ExportDocDockerRuntime {
    [CmdletBinding()]
    param(
        [string]$RuntimeRoot,
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [switch]$Stop,
        [switch]$RestorePending
    )
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
    $markerPath = Join-Path $runtimePath 'native-runtime.json'
    $existingRuntime = Test-Path -LiteralPath $markerPath -PathType Leaf
    if (Test-Path -LiteralPath $runtimePath) {
        if (-not (Test-Path -LiteralPath $markerPath -PathType Leaf) -and @(Get-ChildItem -LiteralPath $runtimePath -Force).Count -gt 0) { throw 'Refusing to adopt an unmarked non-empty runtime directory.' }
        if (Test-Path -LiteralPath $markerPath) {
            $markerFile = Get-Item -LiteralPath $markerPath -Force
            if ($markerFile.PSIsContainer -or ($markerFile.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or $markerFile.Length -gt 16384) { throw 'The native runtime marker must be a regular configuration file.' }
            $marker = Get-Content -LiteralPath $markerPath -Raw | ConvertFrom-Json
            if ($marker.schemaVersion -ne 1 -or $marker.purpose -ne 'exportdoc-rust-native-docker') { throw 'The native runtime marker is invalid.' }
        }
    }
    if (($Stop -or $RestorePending) -and -not $existingRuntime) { throw 'No prepared native Docker runtime exists. Use the original deployment configuration.' }
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
                $file = Get-Item -LiteralPath $path -Force
                if ($file.PSIsContainer -or ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or $file.Length -gt 16384) { throw "Invalid secret file: $Name" }
                if ([System.IO.File]::ReadAllText($path) -ne $Value) { throw "Existing $Name does not match this runtime; it was preserved." }
                return
            }
            if ($existingRuntime) { throw "Existing runtime is missing $Name. Restore the original configuration; credentials were not regenerated." }
            $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try { $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($Value); $stream.Write($bytes); $stream.Flush($true) } finally { $stream.Dispose() }
            if (-not $IsWindows) { [System.IO.File]::SetUnixFileMode($path, [System.IO.UnixFileMode]::UserRead -bor [System.IO.UnixFileMode]::UserWrite -bor [System.IO.UnixFileMode]::GroupRead -bor [System.IO.UnixFileMode]::OtherRead) }
        }
        function Get-NativeSecret {
            param([string]$Name)
            $path = Join-Path $runtimePath $Name
            if (Test-Path -LiteralPath $path) {
                $file = Get-Item -LiteralPath $path -Force
                if ($file.PSIsContainer -or ($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -or $file.Length -ne 64) { throw "Existing $Name is invalid and was preserved." }
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
        [void](Get-NativeSecret -Name 'postgres-bootstrap-password.txt')
        [void](Get-NativeSecret -Name 'bootstrap-token.txt')
        Write-NativeSecret -Name 'app-connection.txt' -Value "host=postgres port=5432 dbname=exportdoc_native user=exportdoc_app password=$appPassword connect_timeout=10 sslmode=disable"
        Write-NativeSecret -Name 'maintenance-connection.txt' -Value "host=postgres port=5432 dbname=exportdoc_native user=exportdoc_maintenance password=$maintenancePassword connect_timeout=10 sslmode=disable"
        $appPassword = $null
        $maintenancePassword = $null
    }
    return $runtimePath
}
