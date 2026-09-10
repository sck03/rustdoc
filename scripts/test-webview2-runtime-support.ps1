[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"
if (-not $IsWindows) {
    Write-Host "WebView2 installer verification tests require Windows."
    return
}

. (Join-Path $PSScriptRoot "lib/microsoft-runtime-support.ps1")
$repositoryRoot = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $repositoryRoot ".codex-runtime/webview2-verification/$([Guid]::NewGuid().ToString('N'))"
$downloadPath = Join-Path $testRoot "MicrosoftEdgeWebView2RuntimeInstallerX64.exe.download"
$script:signatureStatus = [System.Management.Automation.SignatureStatus]::Valid

# Stub only the Windows trust/PE metadata boundary. Size and SHA-256 use real files.
function Get-Item {
    param([string]$LiteralPath)
    $file = Microsoft.PowerShell.Management\Get-Item -LiteralPath $LiteralPath
    [pscustomobject]@{
        Name = $file.Name
        Length = $file.Length
        VersionInfo = [pscustomobject]@{
            CompanyName = "Microsoft Corporation"
            OriginalFilename = "MicrosoftEdgeUpdateSetup.exe"
            FileVersion = "1.2.3.4"
        }
    }
}

function Get-AuthenticodeSignature {
    param([string]$LiteralPath)
    [pscustomobject]@{
        Status = $script:signatureStatus
        SignerCertificate = [pscustomobject]@{ Subject = "CN=Microsoft Corporation, O=Microsoft Corporation, C=US" }
    }
}

function Assert-Rejected {
    param([scriptblock]$Action, [string]$ExpectedMessage)
    try { & $Action | Out-Null }
    catch {
        if (-not $_.Exception.Message.Contains($ExpectedMessage)) { throw }
        return
    }
    throw "Invalid WebView2 installer was accepted; expected: $ExpectedMessage"
}

New-Item -ItemType Directory -Path $testRoot -Force | Out-Null
try {
    [IO.File]::WriteAllBytes($downloadPath, [byte[]](1, 2, 3, 4))
    $release = [pscustomobject]@{
        fileName = "MicrosoftEdgeWebView2RuntimeInstallerX64.exe"
        bytes = 4
        fileVersion = "1.2.3.4"
        originalFileName = "MicrosoftEdgeUpdateSetup.exe"
        sha256 = (Get-FileHash -LiteralPath $downloadPath -Algorithm SHA256).Hash
    }
    $verified = Assert-ExportDocMicrosoftRuntimeFile -Path $downloadPath -Release $release
    if ($verified.Hash -ne $release.sha256) { throw "Verified download hash changed." }

    $script:signatureStatus = [System.Management.Automation.SignatureStatus]::NotSigned
    Assert-Rejected { Assert-ExportDocMicrosoftRuntimeFile -Path $downloadPath -Release $release } "valid Microsoft"
    $script:signatureStatus = [System.Management.Automation.SignatureStatus]::Valid

    [IO.File]::WriteAllBytes($downloadPath, [byte[]](4, 3, 2, 1))
    Assert-Rejected { Assert-ExportDocMicrosoftRuntimeFile -Path $downloadPath -Release $release } "SHA-256"
    [IO.File]::WriteAllBytes($downloadPath, [byte[]](1))
    Assert-Rejected { Assert-ExportDocMicrosoftRuntimeFile -Path $downloadPath -Release $release } "size"
    Write-Host "WebView2 verification tests passed: temporary download name, signature, hash and size."
} finally {
    Remove-Item -LiteralPath $downloadPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $testRoot -ErrorAction Stop
}
