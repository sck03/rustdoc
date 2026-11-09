[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$SampleDirectory)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$samples = (Resolve-Path -LiteralPath $SampleDirectory).Path
$acdSchema = [System.Xml.XmlDocument]::new()
$acdSchema.Load((Join-Path $root 'AcdAgrInfo_ImportSave.xsd'))
$ns = [System.Xml.XmlNamespaceManager]::new($acdSchema.NameTable)
$ns.AddNamespace('xs', 'http://www.w3.org/2001/XMLSchema')
$curr = $acdSchema.SelectNodes('//xs:element[@name="ImportInfo"]/xs:complexType/xs:sequence/xs:element[@name="Curr"]', $ns)
if ($curr.Count -ne 2) { throw 'ACD reference changed: review its duplicate Curr declaration before validation.' }
# The retained reference repeats Curr before OriCountry and after AgentCode.
# Validate the existing single-Curr wire format without modifying the original reference.
[void]$curr[0].ParentNode.RemoveChild($curr[0])
Write-Warning 'ACD validation excludes the first duplicate Curr in memory; raw reference XSD remains inconsistent. This is not official acceptance.'
$files = @(Get-ChildItem -LiteralPath $samples -Filter 'SW-TEST-*-*.xml' -File)
if ($files.Count -ne 7) { throw 'Expected one ACD and six COO sample files.' }
foreach ($file in $files) {
    $settings = [System.Xml.XmlReaderSettings]::new()
    $settings.DtdProcessing = [System.Xml.DtdProcessing]::Prohibit
    $settings.XmlResolver = $null
    $settings.ValidationType = [System.Xml.ValidationType]::Schema
    if ($file.BaseName.EndsWith('-ACD')) {
        $schemaReader = [System.Xml.XmlNodeReader]::new($acdSchema)
        try { [void]$settings.Schemas.Add($null, $schemaReader) } finally { $schemaReader.Dispose() }
    } else {
        [void]$settings.Schemas.Add('http://www.w3.org/2000/09/xmldsig#', (Join-Path $root 'coo.xsd'))
    }
    $reader = [System.Xml.XmlReader]::Create($file.FullName, $settings)
    try { while ($reader.Read()) {} } finally { $reader.Dispose() }
    Write-Output "$($file.Name): valid"
}
