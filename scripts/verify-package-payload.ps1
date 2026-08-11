[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$PackageRoot,
    [Parameter(Mandatory = $true)][ValidateSet("Desktop", "Server", "Container")][string]$Profile,
    [Parameter(Mandatory = $true)][string]$RuntimeIdentifier,
    [ValidateSet("Document", "Sales", "Full")][string]$Edition = "Full"
)

$ErrorActionPreference = "Stop"
$root = [IO.Path]::GetFullPath($PackageRoot)
if (-not (Test-Path -LiteralPath $root -PathType Container)) { throw "Package root does not exist: $root" }
$requiresDocumentResources = $true
$requiresOcrRuntime = $true
$requiresBrowserRuntime = $Profile -ne "Container"
$requiresExcelAnalyzer = $true

$allEntries = @(Get-ChildItem -LiteralPath $root -Force -Recurse)
$forbiddenTopLevelDirectories = @("App_Data", "Database", "Security", "Backups", "Cache", "Config", "Logs", "WebView")
$forbiddenExactFileNames = @(
    "appsettings.json",
    "license.dat",
    "local-master-key.bin",
    "station.id",
    "machine-id.seed",
    "machine-binding.dat",
    "machine-trial-anchor.dat",
    "license-reactivation-required.json",
    "runtime-paths.json",
    "runtime-paths.json.bak",
    "pending-data-root-migration.json",
    ".exportdoc-data-root-migration-complete",
    "pending-disaster-recovery.json"
)
$sensitiveEntries = @($allEntries | Where-Object {
    $relativePath = [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
    $segments = @($relativePath.Split('/', [StringSplitOptions]::RemoveEmptyEntries))
    $topLevel = if ($segments.Count -gt 0) { $segments[0] } else { "" }
    $isForbiddenTopLevel = $forbiddenTopLevelDirectories -contains $topLevel
    $isForbiddenRuntimeDirectory = $_.PSIsContainer -and (
        $_.Name -match '(?i)^\.?.*exportdoc-migration-.*\.staging$' -or
        $_.Name -match '(?i)^pending-[0-9a-f]{32}$')
    $isForbiddenFile = -not $_.PSIsContainer -and (
        $forbiddenExactFileNames -contains $_.Name -or
        $_.Name -match '(?i)\.(?:db|sqlite|sqlite3)(?:-(?:wal|shm))?$' -or
        $_.Name -match '(?i)\.restore-pending\.(?:db|json)$' -or
        $_.Name -match '(?i)\.(?:edmrecovery|swpkg|edpkg)$' -or
        $_.Extension -eq ".log")
    $isForbiddenTopLevel -or $isForbiddenRuntimeDirectory -or $isForbiddenFile
})
if ($sensitiveEntries.Count) {
    $relativeSensitiveEntries = @($sensitiveEntries | ForEach-Object {
        [IO.Path]::GetRelativePath($root, $_.FullName).Replace('\', '/')
    })
    throw "Release payload contains runtime data, credentials, database files, restore staging, or logs: $($relativeSensitiveEntries -join '; ')"
}

if ($Profile -eq "Desktop" -and $RuntimeIdentifier.StartsWith("linux-", [StringComparison]::OrdinalIgnoreCase)) {
    $legacyLttngProviders = @($allEntries | Where-Object {
        -not $_.PSIsContainer -and $_.Name -eq "libcoreclrtraceptprovider.so"
    })
    if ($legacyLttngProviders.Count) {
        throw "Linux desktop payload contains the optional CoreCLR LTTng provider that requires unavailable liblttng-ust.so.0: $($legacyLttngProviders.FullName -join '; ')"
    }
}

if ($Profile -eq "Desktop") {
    $editionManifestPath = Join-Path $root "product-edition.json"
    if (-not (Test-Path -LiteralPath $editionManifestPath -PathType Leaf)) {
        throw "Desktop payload is missing product-edition.json."
    }

    $editionManifest = Get-Content -LiteralPath $editionManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$editionManifest.edition -ne $Edition) {
        throw "Desktop payload edition mismatch: expected $Edition, received $($editionManifest.edition)."
    }

    foreach ($profileKey in @("browserRenderer", "ocr", "documentResources", "excelAnalyzer")) {
        $profileProperty = $editionManifest.resourceProfile.PSObject.Properties[$profileKey]
        if ($null -eq $profileProperty -or $profileProperty.Value -isnot [bool]) {
            throw "Desktop payload resource profile '$profileKey' is missing or invalid."
        }
    }

    $requiresDocumentResources = [bool]$editionManifest.resourceProfile.documentResources
    $requiresOcrRuntime = [bool]$editionManifest.resourceProfile.ocr
    $requiresBrowserRuntime = [bool]$editionManifest.resourceProfile.browserRenderer
    $requiresExcelAnalyzer = [bool]$editionManifest.resourceProfile.excelAnalyzer
    $directoryCapabilities = [ordered]@{
        Templates = $requiresDocumentResources
        Resources = $requiresDocumentResources
        OcrModels = $requiresOcrRuntime
        Browsers = $requiresBrowserRuntime
        Tools = $requiresExcelAnalyzer
    }
    foreach ($directoryName in $directoryCapabilities.Keys) {
        $directoryExists = Test-Path -LiteralPath (Join-Path $root $directoryName) -PathType Container
        if ($directoryExists -ne [bool]$directoryCapabilities[$directoryName]) {
            throw "Desktop/$Edition payload directory '$directoryName' does not match the declared resource profile."
        }
    }

    $ocrSidecars = @($allEntries | Where-Object { -not $_.PSIsContainer -and $_.Name -match '^exportdoc-ocr(?:\.exe)?$' })
    $excelAnalyzers = @($allEntries | Where-Object { -not $_.PSIsContainer -and $_.Name -match '^exportdoc-excel-analyzer(?:\.exe)?$' })
    $playwrightRoots = @($allEntries | Where-Object { $_.PSIsContainer -and $_.Name -eq ".playwright" })
    $expectedOcrSidecarCount = if ($requiresOcrRuntime) { 1 } else { 0 }
    $expectedExcelAnalyzerCount = if ($requiresExcelAnalyzer) { 1 } else { 0 }
    $expectedPlaywrightRootCount = if ($requiresBrowserRuntime) { 1 } else { 0 }
    if ($ocrSidecars.Count -ne $expectedOcrSidecarCount) {
        throw "Desktop/$Edition payload expected $expectedOcrSidecarCount OCR sidecar, found $($ocrSidecars.Count)."
    }
    if ($excelAnalyzers.Count -ne $expectedExcelAnalyzerCount) {
        throw "Desktop/$Edition payload expected $expectedExcelAnalyzerCount Excel analyzer, found $($excelAnalyzers.Count)."
    }
    if ($playwrightRoots.Count -ne $expectedPlaywrightRootCount) {
        throw "Desktop/$Edition payload expected $expectedPlaywrightRootCount Playwright runtime root, found $($playwrightRoots.Count)."
    }
}

$repositoryRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ".."))
$fontManifestPath = Join-Path $repositoryRoot "Resources/Fonts/OpenSource/font-manifest.json"
if (-not (Test-Path -LiteralPath $fontManifestPath -PathType Leaf)) { throw "Approved font manifest is missing: $fontManifestPath" }
$fontManifest = Get-Content -LiteralPath $fontManifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
$approvedFonts = @{}
foreach ($font in @($fontManifest.fonts)) {
    $approvedFonts[[string]$font.fileName.ToLowerInvariant()] = $font
}
$fontExtensions = @(".ttf", ".otf", ".ttc", ".woff", ".woff2", ".eot")
$forbiddenFontNamePattern = '(?i)(msyh|microsoft[ _-]*yahei|simsun|simhei|segoe|arial|times[ _-]*new[ _-]*roman|sf[ _-]*pro|pingfang|hiragino|consolas)'
$packageFontRoot = [IO.Path]::GetFullPath((Join-Path $root "Resources/Fonts/OpenSource"))
$fontFiles = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object { $fontExtensions -contains $_.Extension.ToLowerInvariant() })

foreach ($fontFile in $fontFiles) {
    if ($fontFile.Name -match $forbiddenFontNamePattern) {
        throw "Release payload contains a proprietary or forbidden font binary: $($fontFile.FullName)"
    }

    $fontKey = $fontFile.Name.ToLowerInvariant()
    if (-not $approvedFonts.ContainsKey($fontKey)) {
        throw "Release payload contains a font that is not registered in font-manifest.json: $($fontFile.FullName)"
    }

    $fontFullPath = [IO.Path]::GetFullPath($fontFile.FullName)
    $expectedFontPrefix = $packageFontRoot.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if (-not $fontFullPath.StartsWith($expectedFontPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Approved report fonts must stay under Resources/Fonts/OpenSource: $fontFullPath"
    }

    $actualHash = (Get-FileHash -LiteralPath $fontFullPath -Algorithm SHA256).Hash.ToUpperInvariant()
    $expectedHash = ([string]$approvedFonts[$fontKey].sha256).ToUpperInvariant()
    if ($actualHash -ne $expectedHash) {
        throw "Font SHA-256 mismatch for '$($fontFile.Name)'. Expected $expectedHash, received $actualHash."
    }
}

if ($Profile -ne "Container" -and $requiresDocumentResources) {
    foreach ($font in @($fontManifest.fonts)) {
        $matches = @($fontFiles | Where-Object Name -eq ([string]$font.fileName))
        if ($matches.Count -ne 1) {
            throw "$Profile payload must contain exactly one approved font '$($font.fileName)', found $($matches.Count)."
        }
    }

    foreach ($noticeName in @("font-manifest.json", [string]$fontManifest.licenseFile, "README.md")) {
        $noticePath = Join-Path $packageFontRoot $noticeName
        if (-not (Test-Path -LiteralPath $noticePath -PathType Leaf)) {
            throw "$Profile payload is missing report font policy file: $noticePath"
        }
    }

    $packageLicensePath = Join-Path $packageFontRoot ([string]$fontManifest.licenseFile)
    $packageLicenseHash = (Get-FileHash -LiteralPath $packageLicensePath -Algorithm SHA256).Hash.ToUpperInvariant()
    if ($packageLicenseHash -ne ([string]$fontManifest.licenseSha256).ToUpperInvariant()) {
        throw "$Profile payload contains a modified or incomplete font license: $packageLicensePath"
    }
}

$requiredLegalFiles = if ($Profile -eq "Container") {
    @("THIRD_PARTY_NOTICES.md", "THIRD_PARTY_DEPENDENCIES.md")
} else {
    @(
        "THIRD_PARTY_NOTICES.md",
        "THIRD_PARTY_DEPENDENCIES.md",
        "exportdocmanager.spdx.json",
        "exportdocmanager.cyclonedx.json"
    )
}
$legalRoot = Join-Path $root "Legal"
foreach ($legalFile in $requiredLegalFiles) {
    $legalPath = Join-Path $legalRoot $legalFile
    if (-not (Test-Path -LiteralPath $legalPath -PathType Leaf) -or (Get-Item -LiteralPath $legalPath).Length -le 0) {
        throw "$Profile payload is missing the audited third-party legal file: $legalPath"
    }
}

if ($Profile -eq "Server") {
    $requiredServerEntries = if ($RuntimeIdentifier.StartsWith("win-", [StringComparison]::OrdinalIgnoreCase)) {
        @("README.md", "version.json", "appsettings.example.json", "initialize-windows.ps1", "start-windows.ps1", "setup-windows.cmd", "start-windows.cmd", "wwwroot/index.html")
    } else {
        @("README.md", "version.json", "appsettings.example.json", "initialize-linux.sh", "start-linux.sh", "wwwroot/index.html")
    }
    foreach ($relativeEntry in $requiredServerEntries) {
        $entryPath = Join-Path $root $relativeEntry
        if (-not (Test-Path -LiteralPath $entryPath -PathType Leaf)) {
            throw "Server payload is missing deployment entry '$relativeEntry': $entryPath"
        }
    }
}

$runtimeName = if ($RuntimeIdentifier.StartsWith("win-")) { "onnxruntime.dll" } elseif ($RuntimeIdentifier.StartsWith("osx-")) { "libonnxruntime.dylib" } else { "libonnxruntime.so" }
$runtimeFiles = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object Name -eq $runtimeName)
if ($requiresOcrRuntime -and $runtimeFiles.Count -ne 1) { throw "Expected exactly one shared $runtimeName, found $($runtimeFiles.Count)." }
if (-not $requiresOcrRuntime -and $runtimeFiles.Count -ne 0) { throw "Desktop/$Edition payload must not contain OCR runtime $runtimeName." }

foreach ($relative in @("OcrModels/PaddleOCR/V6/det/inference.onnx", "OcrModels/PaddleOCR/V6/rec/inference.onnx")) {
    $matches = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object { $_.FullName.Replace('\','/').EndsWith($relative, [StringComparison]::OrdinalIgnoreCase) })
    $expectedCount = if ($requiresOcrRuntime) { 1 } else { 0 }
    if ($matches.Count -ne $expectedCount) { throw "Expected $expectedCount OCR model '$relative', found $($matches.Count)." }
}

$browserRoots = @(Get-ChildItem -LiteralPath $root -Directory -Recurse | Where-Object Name -eq "Browsers")
if ($Profile -eq "Container" -or -not $requiresBrowserRuntime) {
    if ($browserRoots.Count -ne 0) { throw "$Profile/$Edition payload must not bundle Browsers/." }
} elseif ($browserRoots.Count -ne 1) {
    throw "$Profile payload must contain exactly one Browsers root, found $($browserRoots.Count)."
}

if ($Profile -ne "Container" -and $requiresBrowserRuntime) {
    $browserRoot = $browserRoots[0].FullName
    $expectedPlatform = switch ($RuntimeIdentifier) {
        "win-x64" { "win64" }
        "linux-x64" { "linux64" }
        "linux-arm64" { "ChromiumArm64" }
        "osx-arm64" { "mac-arm64" }
        default { throw "No browser payload mapping for $RuntimeIdentifier." }
    }
    $platformDirectories = if ($RuntimeIdentifier -eq "linux-arm64") {
        @(Get-ChildItem -LiteralPath $browserRoot -Directory | Where-Object Name -eq "ChromiumArm64")
    } else {
        $chromeRoot = Join-Path $browserRoot "ChromeForTesting"
        if (-not (Test-Path -LiteralPath $chromeRoot -PathType Container)) { throw "ChromeForTesting root is missing for $RuntimeIdentifier." }
        @(Get-ChildItem -LiteralPath $chromeRoot -Directory)
    }
    if ($platformDirectories.Count -ne 1 -or $platformDirectories[0].Name -ne $expectedPlatform) {
        throw "Browser payload must contain only '$expectedPlatform'; found '$($platformDirectories.Name -join ', ')'."
    }

    $browserNoticeFiles = @(Get-ChildItem -LiteralPath $browserRoot -File -Recurse | Where-Object {
        $_.Name -match '(?i)(?:license|notice|copying)'
    })
    if ($browserNoticeFiles.Count -eq 0) {
        throw "$Profile browser payload is missing its upstream license or third-party notice file."
    }
}

$duplicateNativeNames = @(Get-ChildItem -LiteralPath $root -File -Recurse |
    Where-Object { $_.Name -match '^(onnxruntime\.dll|libonnxruntime(?:\.[0-9.]+)?\.(?:so|dylib))$' } |
    Group-Object Name | Where-Object Count -gt 1)
if ($duplicateNativeNames.Count) { throw "Duplicate ONNX Runtime native files: $($duplicateNativeNames.Name -join ', ')" }

$forbiddenPayload = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object {
    $_.Extension -eq ".pdb" -or $_.Name -match 'onnxruntime_providers_shared|^Microsoft\.ML\.OnnxRuntime\.dll$|^onnxruntime\.lib$'
})
if ($forbiddenPayload.Count) { throw "Release payload contains removable debug or duplicate ONNX files: $($forbiddenPayload.FullName -join '; ')" }

$forbiddenDeveloperUiPayload = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object {
    $_.FullName.Replace('\', '/') -match '(?i)/\.playwright/package/lib/vite/(dashboard|recorder|traceViewer)/'
})
if ($forbiddenDeveloperUiPayload.Count) {
    throw "Release payload contains Playwright developer UI files: $($forbiddenDeveloperUiPayload.FullName -join '; ')"
}

$forbiddenPrivateToolPayload = @(Get-ChildItem -LiteralPath $root -File -Recurse | Where-Object {
    $_.Name -match '(?i)^ExportDocLicenseKeyGen(?:\.|-)'
})
if ($forbiddenPrivateToolPayload.Count) {
    throw "Customer release payload contains the private license key generator: $($forbiddenPrivateToolPayload.FullName -join '; ')"
}

$files = @(Get-ChildItem -LiteralPath $root -File -Recurse)
$bytes = ($files | Measure-Object Length -Sum).Sum
$sharedOnnxRuntime = if ($runtimeFiles.Count -eq 1) { $runtimeFiles[0].FullName } else { "not-included" }
Write-Host "Package payload verified: Profile=$Profile Edition=$Edition RID=$RuntimeIdentifier Files=$($files.Count) Bytes=$bytes Fonts=$($fontFiles.Count) SharedOnnxRuntime=$sharedOnnxRuntime"
