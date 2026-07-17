[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
. (Join-Path $scriptRoot "lib\build-script-support.ps1")
trap {
    Write-ExportDocScriptFailure -ErrorRecord $_
    exit 1
}

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $scriptRoot "..")).Path
$runner = Join-Path $scriptRoot "run-tauri-local.ps1"
$results = New-Object System.Collections.Generic.List[object]
$powerShellExecutable = Resolve-ExportDocPowerShellExecutable

foreach ($edition in @("Document", "Sales", "Full")) {
    $startedAt = Get-Date
    Invoke-ExportDocExternal -FilePath $powerShellExecutable -Arguments @(
        "-NoLogo", "-NoProfile", "-ExecutionPolicy", "Bypass",
        "-File", $runner,
        "cargo-check",
        "-ProductEdition", $edition
    )

    $results.Add([pscustomobject]@{
        Edition = $edition
        Success = $true
        ElapsedSeconds = [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 2)
    })
}

[pscustomobject]@{
    Success = $true
    Workspace = $repoRoot
    Editions = $results
} | ConvertTo-Json -Depth 4
