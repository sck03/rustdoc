[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [string]$RemoteUrl,
    [string]$DefaultBranch = "main",
    [string]$CommitMessage = "Initial public source import",
    [switch]$CreateCommit,
    [switch]$Push,
    [switch]$ReplaceOrigin
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $scriptRoot "..\.."
}
$repositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$guardScript = Join-Path $scriptRoot "verify-public-source.ps1"

& git --version *> $null
if ($LASTEXITCODE -ne 0) {
    throw "Git was not found. Install Git for Windows or GitHub Desktop first."
}

& git -C $repositoryRoot rev-parse --is-inside-work-tree *> $null
if ($LASTEXITCODE -ne 0) {
    & git -C $repositoryRoot init --initial-branch=$DefaultBranch
    if ($LASTEXITCODE -ne 0) {
        throw "git init failed."
    }
}

& pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $guardScript -RepositoryRoot $repositoryRoot
if ($LASTEXITCODE -ne 0) {
    throw "Public-source guard failed before staging."
}

& git -C $repositoryRoot add --all
if ($LASTEXITCODE -ne 0) {
    throw "git add failed."
}

& pwsh -NoLogo -NoProfile -ExecutionPolicy Bypass -File $guardScript -RepositoryRoot $repositoryRoot
if ($LASTEXITCODE -ne 0) {
    throw "Public-source guard failed after staging."
}

if (-not [string]::IsNullOrWhiteSpace($RemoteUrl)) {
    $currentOrigin = (& git -C $repositoryRoot remote get-url origin 2>$null)
    if ($LASTEXITCODE -eq 0) {
        if (-not [string]::Equals($currentOrigin.Trim(), $RemoteUrl.Trim(), [System.StringComparison]::OrdinalIgnoreCase)) {
            if (-not $ReplaceOrigin) {
                throw "origin already points to '$currentOrigin'. Use -ReplaceOrigin to change it."
            }
            & git -C $repositoryRoot remote set-url origin $RemoteUrl
        }
    } else {
        & git -C $repositoryRoot remote add origin $RemoteUrl
    }
}

if ($CreateCommit) {
    & git -C $repositoryRoot diff --cached --quiet
    if ($LASTEXITCODE -ne 0) {
        & git -C $repositoryRoot commit -m $CommitMessage
        if ($LASTEXITCODE -ne 0) {
            throw "git commit failed. Configure git user.name and user.email, then retry."
        }
    }
}

if ($Push) {
    if (-not $CreateCommit) {
        throw "-Push requires -CreateCommit so the script never pushes an unstated commit."
    }
    & git -C $repositoryRoot remote get-url origin *> $null
    if ($LASTEXITCODE -ne 0) {
        throw "origin is not configured. Pass -RemoteUrl first."
    }
    & git -C $repositoryRoot push -u origin $DefaultBranch
    if ($LASTEXITCODE -ne 0) {
        throw "git push failed. Check GitHub authentication and repository permissions."
    }
}

Write-Host "GitHub repository preparation completed."
& git -C $repositoryRoot status --short --branch
