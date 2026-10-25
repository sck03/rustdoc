. (Join-Path $PSScriptRoot 'native-ocr-resources.ps1')
. (Join-Path $PSScriptRoot 'native-runtime-resources.ps1')

function Copy-ExportDocNativePackageFiles {
    param(
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Copies,
        [Parameter(Mandatory = $true)][string]$OutputRoot
    )
    Assert-NativePackagePath -Path $OutputRoot
    $manifestPath = Join-Path $OutputRoot 'exportdoc-package-files.json'
    Assert-NativePackagePath -Path $manifestPath -File
    $comparer = if ((Get-ExportDocPathComparison) -eq [StringComparison]::OrdinalIgnoreCase) { [StringComparer]::OrdinalIgnoreCase } else { [StringComparer]::Ordinal }
    $currentFiles = [Collections.Generic.Dictionary[string,string]]::new($comparer)
    $previousFiles = [Collections.Generic.Dictionary[string,string]]::new($comparer)
    # Validate the complete copy and removal plan before changing an existing package.
    foreach ($copy in $Copies.GetEnumerator()) {
        $destination = Resolve-ExportDocNativePackageFile -RelativePath $copy.Value -OutputRoot $OutputRoot
        Assert-ExportDocNativePackageResource -RelativePath $copy.Value
        Assert-NativePackagePath -Path $copy.Key
        if (-not (Test-Path -LiteralPath $copy.Key -PathType Leaf)) { throw "Package input is missing: $($copy.Key)" }
        if (Test-Path -LiteralPath $destination -PathType Container) { throw "Package file destination is a directory: $($copy.Value)" }
        $relative = [IO.Path]::GetRelativePath($OutputRoot, $destination)
        if (-not $currentFiles.TryAdd($relative, (Get-FileHash -LiteralPath $copy.Key -Algorithm SHA256).Hash)) { throw "Duplicate package destination: $($copy.Value)" }
    }
    if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        if ($manifest.schemaVersion -ne 1 -or $manifest.files -isnot [array]) { throw 'Invalid native package file manifest.' }
        foreach ($file in $manifest.files) {
            if ($file.path -isnot [string] -or $file.sha256 -isnot [string] -or $file.sha256 -notmatch '^[A-Fa-f0-9]{64}$') { throw 'Invalid native package resource identity.' }
            Assert-ExportDocNativePackageResource -RelativePath $file.path
            $destination = Resolve-ExportDocNativePackageFile -RelativePath $file.path -OutputRoot $OutputRoot
            if (-not $previousFiles.TryAdd([IO.Path]::GetRelativePath($OutputRoot, $destination), $file.sha256)) { throw 'Duplicate native package manifest file.' }
        }
    }
    # Callers validate the package identity before this copy. These immutable
    # runtime trees belong to the packager, including files predating its manifest.
    # Custom templates and runtime data are never swept as directories.
    foreach ($relativeRoot in @('Web', 'sidecar/ocr', 'OcrModels/PaddleOCR/V6', 'Resources/Pdf', 'Tools/PostgreSQL', 'WebView2Runtime')) {
        $ownedRoot = Resolve-ExportDocNativePackageFile -RelativePath $relativeRoot -OutputRoot $OutputRoot
        if (Test-Path -LiteralPath $ownedRoot -PathType Container) {
            foreach ($file in Get-ExportDocNativePackageOwnedFiles -Directory $ownedRoot -OutputRoot $OutputRoot) {
                $relative = [IO.Path]::GetRelativePath($OutputRoot, $file)
                if (-not $previousFiles.ContainsKey($relative)) { $previousFiles.Add($relative, (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash) }
            }
        }
    }
    foreach ($file in $previousFiles.GetEnumerator()) {
        $destination = Resolve-ExportDocNativePackageFile -RelativePath $file.Key -OutputRoot $OutputRoot
        if (Test-Path -LiteralPath $destination -PathType Container) { throw "Package resource became a directory: $($file.Key)" }
        if ((Test-Path -LiteralPath $destination -PathType Leaf) -and (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $file.Value) {
            throw "Package resource was modified; preserve or move it before rebuilding: $($file.Key)"
        }
    }
    foreach ($file in $currentFiles.GetEnumerator()) {
        # A first rebuild cannot distinguish an old built-in template from a
        # manually edited template. Adopt identical files; preserve conflicts.
        if ($file.Key -match '^Templates[\\/]' -and -not $previousFiles.ContainsKey($file.Key)) {
            $destination = Resolve-ExportDocNativePackageFile -RelativePath $file.Key -OutputRoot $OutputRoot
            if ((Test-Path -LiteralPath $destination -PathType Leaf) -and (Get-FileHash -LiteralPath $destination -Algorithm SHA256).Hash -ne $file.Value) {
                throw "Unowned template differs from the package; preserve or move it before rebuilding: $($file.Key)"
            }
        }
    }
    foreach ($copy in $Copies.GetEnumerator()) {
        $destination = Join-Path $OutputRoot $copy.Value
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $destination) | Out-Null
        Copy-Item -LiteralPath $copy.Key -Destination $destination -Force
    }
    foreach ($relative in $previousFiles.Keys) {
        if (-not $currentFiles.ContainsKey($relative)) {
            $obsolete = Resolve-ExportDocNativePackageFile -RelativePath $relative -OutputRoot $OutputRoot
            if (Test-Path -LiteralPath $obsolete -PathType Leaf) { Remove-Item -LiteralPath $obsolete -Force }
        }
    }
    $temporaryManifest = "$manifestPath.$([Guid]::NewGuid().ToString('N')).tmp"
    try {
        @{ schemaVersion = 1; files = @($currentFiles.Keys | Sort-Object | ForEach-Object { @{ path = $_; sha256 = $currentFiles[$_] } }) } | ConvertTo-Json -Depth 4 |
            Set-Content -LiteralPath $temporaryManifest -Encoding utf8
        [IO.File]::Move($temporaryManifest, $manifestPath, $true)
    } finally {
        if (Test-Path -LiteralPath $temporaryManifest -PathType Leaf) { Remove-Item -LiteralPath $temporaryManifest -Force }
    }
}

function Assert-ExportDocNativePackageResource {
    param([Parameter(Mandatory = $true)][string]$RelativePath)
    $portablePath = $RelativePath.Replace('\', '/')
    if ($portablePath -notmatch '^(?:ExportDocManager(?:\.Server)?(?:\.exe)?|WebView2Loader\.dll|THIRD_PARTY_(?:NOTICES|DEPENDENCIES)\.md)$' -and
        $portablePath -notmatch '^(?:Web|sidecar/ocr|OcrModels/PaddleOCR/V6|Resources/(?:Fonts/OpenSource|ExcelTemplates|Pdf)|Tools/PostgreSQL|WebView2Runtime)/.+' -and
        $portablePath -notmatch '^Templates/.+\.dtpl$') {
        throw "Path is not an owned application resource: $RelativePath"
    }
}

function Get-ExportDocNativePackageOwnedFiles {
    param([Parameter(Mandatory = $true)][string]$Directory, [Parameter(Mandatory = $true)][string]$OutputRoot)
    # Inspect each direct child before descending, never traverse a link or Git boundary.
    foreach ($entry in Get-ChildItem -LiteralPath $Directory -Force) {
        $relative = [IO.Path]::GetRelativePath($OutputRoot, $entry.FullName)
        [void](Resolve-ExportDocNativePackageFile -RelativePath $relative -OutputRoot $OutputRoot)
        if ($entry.PSIsContainer) { Get-ExportDocNativePackageOwnedFiles -Directory $entry.FullName -OutputRoot $OutputRoot }
        else { $entry.FullName }
    }
}

function Resolve-ExportDocNativePackageFile {
    param([Parameter(Mandatory = $true)][string]$RelativePath, [Parameter(Mandatory = $true)][string]$OutputRoot)
    $segments = $RelativePath -split '[\\/]'
    $invalidSegment = @($segments | Where-Object { $_ -match '[<>:"|?*\x00-\x1F]|[ .]$|^(?:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)' }).Count -gt 0
    if ($invalidSegment -or [IO.Path]::IsPathRooted($RelativePath) -or $segments -contains '' -or $segments -contains '..' -or $segments -contains '.' -or
        $segments[0] -in @('.git', '.codex-runtime', 'App_Data', 'Data', 'Backups', 'exportdoc-package-files.json')) {
        throw "Package file must be a relative application resource: $RelativePath"
    }
    $destination = [IO.Path]::GetFullPath((Join-Path $OutputRoot $RelativePath))
    if (-not (Test-ExportDocPathUnderRoot -Path $destination -Root $OutputRoot)) { throw 'Package destination escaped output root.' }
    Assert-NativePackagePath -Path $destination
    $boundary = $destination
    while (Test-ExportDocPathUnderRoot -Path $boundary -Root $OutputRoot -AllowRoot) {
        if (Test-Path -LiteralPath (Join-Path $boundary '.git')) { throw "Package path contains a Git checkout: $boundary" }
        if (Test-ExportDocPathEqual -Left $boundary -Right $OutputRoot) { break }
        $boundary = [IO.Path]::GetDirectoryName($boundary)
    }
    return $destination
}

function Assert-NativePackagePath {
    param([Parameter(Mandatory = $true)][string]$Path, [switch]$File)
    $nativePath = [System.IO.Path]::GetFullPath($Path)
    if ($nativePath -eq [System.IO.Path]::GetPathRoot($nativePath)) { throw 'A native package path must not be a disk root.' }
    $currentPath = $nativePath
    while ($currentPath) {
        if (Test-Path -LiteralPath $currentPath) {
            $entry = Get-Item -LiteralPath $currentPath -Force
            if ($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) { throw "Linked paths are not allowed: $currentPath" }
        }
        $parentPath = [System.IO.Path]::GetDirectoryName($currentPath)
        if ($parentPath -eq $currentPath) { break }
        $currentPath = $parentPath
    }
    if ($File -and (Test-Path -LiteralPath $nativePath -PathType Container)) { throw "Package file path is a directory: $Path" }
}
function Add-ExportDocRustPackageResources {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Copies,
        [string]$RustTarget,
        [string]$PdfiumPath,
        [string]$OnnxRuntimePath,
        [bool]$DocumentResources = $true,
        [switch]$WithoutOcr,
        [switch]$SkipBuild
    )
    Invoke-ExportDocExternal -FilePath 'node' -Arguments @((Join-Path $RepositoryRoot 'scripts/provision-report-fonts.mjs')) -WorkingDirectory $RepositoryRoot -DisplayName 'Provision verified report fonts'
    if (-not $WithoutOcr) {
        Add-ExportDocNativeOcrResources -RepositoryRoot $repositoryRoot -Configuration $Configuration -Copies $copies -OnnxRuntimePath $OnnxRuntimePath -RustTarget $RustTarget -SkipBuild:$SkipBuild
    }
    foreach ($name in @('NotoSansCJKsc-Regular.otf', 'NotoSansCJKsc-Bold.otf', 'NotoSerifCJKsc-Regular.otf', 'OFL-Noto-CJK.txt', 'font-manifest.json')) {
        $copies[(Join-Path $repositoryRoot "Resources/Fonts/OpenSource/$name")] = "Resources/Fonts/OpenSource/$name"
    }
    if ($DocumentResources) { $copies[(Join-Path $repositoryRoot 'Resources/ExcelTemplates/invoice-import-template.xlsx')] = 'Resources/ExcelTemplates/invoice-import-template.xlsx' }
    if ([string]::IsNullOrWhiteSpace($PdfiumPath)) {
        $platform = if ($env:OS -eq 'Windows_NT') { 'win32' } elseif ($IsMacOS) { 'macos' } else { 'linux' }
        $ridPlatform = if ($env:OS -eq 'Windows_NT') { 'win' } elseif ($IsMacOS) { 'osx' } else { 'linux' }
        $nativeName = if ($env:OS -eq 'Windows_NT') { 'pdfium.dll' } elseif ($IsMacOS) { 'libpdfium.dylib' } else { 'libpdfium.so' }
        $architecture = if ($RustTarget -like 'aarch64-*') { 'arm64' } elseif ($RustTarget -like 'x86_64-*') { 'x64' } else { [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant() }
        $PdfiumPath = Get-ExportDocNativeRuntimeLibrary -RepositoryRoot $repositoryRoot -PackageId "bblanchon.pdfium.$platform" -RuntimeIdentifier "$ridPlatform-$architecture" -LibraryName $nativeName
    }
    Assert-NativePackagePath -Path $PdfiumPath
    $copies[$PdfiumPath] = 'Resources/Pdf/' + [System.IO.Path]::GetFileName($PdfiumPath)
    $templateRoot = Join-Path $repositoryRoot 'Templates'
    if ($DocumentResources) {
    foreach ($template in Get-ChildItem -LiteralPath $templateRoot -Recurse -File -Filter '*.dtpl') {
        Assert-NativePackagePath -Path $template.FullName
        $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $template.FullName)
        $copies[$template.FullName] = $relative
    }
    }
    foreach ($name in @('THIRD_PARTY_NOTICES.md', 'THIRD_PARTY_DEPENDENCIES.md')) {
        $copies[(Join-Path $repositoryRoot $name)] = $name
    }
}
