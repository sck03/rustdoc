. (Join-Path $PSScriptRoot 'native-ocr-resources.ps1')
. (Join-Path $PSScriptRoot 'native-runtime-resources.ps1')

function Assert-NativePackagePath {
    param([Parameter(Mandatory = $true)][string]$Path)
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
}
function Add-ExportDocRustPackageResources {
    param(
        [Parameter(Mandatory = $true)][string]$RepositoryRoot,
        [Parameter(Mandatory = $true)][string]$Configuration,
        [Parameter(Mandatory = $true)][System.Collections.IDictionary]$Copies,
        [string]$RustTarget,
        [string]$PdfiumPath,
        [string]$OnnxRuntimePath,
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
    $copies[(Join-Path $repositoryRoot 'Resources/ExcelTemplates/invoice-import-template.xlsx')] = 'Resources/ExcelTemplates/invoice-import-template.xlsx'
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
    foreach ($template in Get-ChildItem -LiteralPath $templateRoot -Recurse -File) {
        Assert-NativePackagePath -Path $template.FullName
        $relative = [System.IO.Path]::GetRelativePath($repositoryRoot, $template.FullName)
        $copies[$template.FullName] = $relative
    }
    foreach ($name in @('THIRD_PARTY_NOTICES.md', 'THIRD_PARTY_DEPENDENCIES.md')) {
        $copies[(Join-Path $repositoryRoot $name)] = $name
    }
}
