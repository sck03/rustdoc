function New-ExportDocWebRuntimeSmokeArguments {
    param(
        [Parameter(Mandatory = $true)][string]$ScriptPath,
        [Parameter(Mandatory = $true)][string]$BrowserExecutable,
        [Parameter(Mandatory = $true)][string]$WebUrl,
        [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
        [Parameter(Mandatory = $true)][string]$DesktopAccessToken,
        [Parameter(Mandatory = $true)][string]$Username,
        [AllowEmptyString()][string]$Password = "",
        [Parameter(Mandatory = $true)][string]$UserDataDirectory,
        [Parameter(Mandatory = $true)][ValidateRange(1, [int]::MaxValue)][int]$TimeoutMilliseconds
    )

    $arguments = New-Object System.Collections.Generic.List[string]
    foreach ($argument in @(
        $ScriptPath,
        "--browser-executable", $BrowserExecutable,
        "--web-url", $WebUrl,
        "--api-base-url", $ApiBaseUrl,
        "--desktop-access-token", $DesktopAccessToken,
        "--mock-tauri-runtime-context",
        "--username", $Username
    )) {
        $arguments.Add([string]$argument)
    }

    if (-not [string]::IsNullOrEmpty($Password)) {
        $arguments.Add("--password")
        $arguments.Add($Password)
    }

    foreach ($argument in @(
        "--user-data-dir", $UserDataDirectory,
        "--timeout-ms", ([string]$TimeoutMilliseconds)
    )) {
        $arguments.Add([string]$argument)
    }

    return ,$arguments
}
