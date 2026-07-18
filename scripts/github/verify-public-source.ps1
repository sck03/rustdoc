[CmdletBinding()]
param(
    [string]$RepositoryRoot,
    [switch]$AsJson
)

$ErrorActionPreference = "Stop"
$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $scriptRoot "..\.."
}
$repositoryRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
if (-not (Test-Path -LiteralPath $repositoryRoot -PathType Container)) {
    throw "Repository root was not found: $repositoryRoot"
}

$requiredIgnoreRules = @(
    "apps/license-keygen-tauri/",
    "Browsers/**",
    "**/KEY/",
    "**/ExportDocLicenseKeyGen*"
)
$ignorePath = Join-Path $repositoryRoot ".gitignore"
$ignoreText = Get-Content -LiteralPath $ignorePath -Raw -Encoding UTF8
$errors = New-Object System.Collections.Generic.List[string]
foreach ($rule in $requiredIgnoreRules) {
    if (-not $ignoreText.Contains($rule, [System.StringComparison]::Ordinal)) {
        $errors.Add(".gitignore is missing required public-release rule: $rule")
    }
}

$candidateFiles = @()
& git -C $repositoryRoot rev-parse --is-inside-work-tree *> $null
if ($LASTEXITCODE -eq 0) {
    $candidateFiles = @(& git -c core.quotepath=false -C $repositoryRoot ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed while enumerating public source candidates."
    }
} else {
    $excludedRoots = @(
        ".git", ".codex-runtime", "artifacts", "tmp", "logs", "TestResults",
        "App_Data", "node_modules", "bin", "obj", "dist", "target",
        "license-keygen-tauri", "ChromeForTesting"
    )
    $candidateFiles = Get-ChildItem -LiteralPath $repositoryRoot -Recurse -File -Force |
        Where-Object {
            $segments = $_.FullName.Substring($repositoryRoot.Length).TrimStart('\', '/') -split '[\\/]'
            -not ($segments | Where-Object { $_ -in $excludedRoots })
        } |
        ForEach-Object { [System.IO.Path]::GetRelativePath($repositoryRoot, $_.FullName) }
}

$forbiddenPathPatterns = @(
    '^apps/license-keygen-tauri/',
    '(^|/)KEY/',
    'ExportDocLicenseKeyGen',
    '\.(pem|key|p8|p12|pfx|snk)$'
)
$textExtensions = @(
    '.cs', '.rs', '.ts', '.tsx', '.js', '.jsx', '.mjs', '.cjs', '.ps1', '.cmd',
    '.yml', '.yaml', '.json', '.xml', '.toml', '.props', '.targets', '.md', '.txt',
    '.html', '.css', '.config', '.example', '.env'
)
$forbiddenContentPatterns = @(
    '-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----',
    'MIGHAgEAMBMGByqGSM49AgEGCCqGSM49AwEHBG0wawIB',
    '\bSIGNATURE_PRIVATE_KEY\b',
    '\bTestSignaturePrivateKey\b',
    'LicenseKeyCodec\.GenerateSigned\s*\('
)

$checkedBytes = [long]0
$checkedFiles = 0
foreach ($relativePathValue in ($candidateFiles | Sort-Object -Unique)) {
    $relativePath = [string]$relativePathValue
    if ([string]::IsNullOrWhiteSpace($relativePath)) {
        continue
    }

    $normalizedPath = $relativePath.Replace('\', '/')
    foreach ($pattern in $forbiddenPathPatterns) {
        if ($normalizedPath -match $pattern) {
            $errors.Add("Forbidden public path: $normalizedPath")
        }
    }

    $absolutePath = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $absolutePath -PathType Leaf)) {
        continue
    }

    # PowerShell treats Unix dotfiles such as .dockerignore as hidden items.
    # Use FileInfo directly so a clean Linux checkout is handled exactly like
    # Windows and does not require provider-specific -Force behavior.
    $file = [System.IO.FileInfo]::new($absolutePath)
    if (-not $file.Exists) {
        continue
    }
    $checkedFiles++
    $checkedBytes += $file.Length
    if ($file.Length -ge 95MB) {
        $errors.Add(("File exceeds the safe GitHub limit (95 MiB guard): {0} ({1:N2} MiB)" -f $normalizedPath, ($file.Length / 1MB)))
    }

    if ($normalizedPath -eq "scripts/github/verify-public-source.ps1" -or
        $file.Length -gt 5MB -or
        $file.Extension.ToLowerInvariant() -notin $textExtensions) {
        continue
    }

    $content = Get-Content -LiteralPath $absolutePath -Raw -Encoding UTF8 -ErrorAction SilentlyContinue
    if ($null -eq $content) {
        continue
    }
    foreach ($pattern in $forbiddenContentPatterns) {
        if ($content -match $pattern) {
            $errors.Add("Forbidden signing material or generator API found in: $normalizedPath")
            break
        }
    }
}

$result = [ordered]@{
    success = $errors.Count -eq 0
    repositoryRoot = $repositoryRoot
    checkedFiles = $checkedFiles
    checkedBytes = $checkedBytes
    errors = @($errors)
}

if ($AsJson) {
    $result | ConvertTo-Json -Depth 4
} else {
    Write-Host "GitHub public-source guard:"
    Write-Host "  Root          : $repositoryRoot"
    Write-Host "  Checked files : $checkedFiles"
    Write-Host ("  Checked size  : {0:N2} MiB" -f ($checkedBytes / 1MB))
    if ($errors.Count -eq 0) {
        Write-Host "  Result        : PASS"
    } else {
        Write-Host "  Result        : FAIL"
        foreach ($errorMessage in $errors) {
            Write-Host "  - $errorMessage"
        }
    }
}

if ($errors.Count -gt 0) {
    exit 1
}
