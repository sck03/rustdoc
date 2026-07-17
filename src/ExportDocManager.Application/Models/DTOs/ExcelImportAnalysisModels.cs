namespace ExportDocManager.Models.DTOs
{
    public sealed class ExcelImportAnalysisReport
    {
        public string SchemaVersion { get; set; } = "excel-analysis-dotnet/1.0";

        public string AnalyzerId { get; set; } = string.Empty;

        public string SourcePath { get; set; } = string.Empty;

        public string SelectedWorksheetName { get; set; } = string.Empty;

        public decimal Confidence { get; set; }

        public List<ExcelImportSheetAnalysis> Sheets { get; set; } = new();

        public List<ExcelImportFieldAnalysis> Fields { get; set; } = new();

        public ExcelImportItemTableAnalysis ItemTable { get; set; }

        public List<ExcelImportAnalysisIssue> Issues { get; set; } = new();
    }

    public sealed class ExcelImportSheetAnalysis
    {
        public string Name { get; set; } = string.Empty;

        public int UsedRowCount { get; set; }

        public int UsedColumnCount { get; set; }

        public int FieldCandidateCount { get; set; }

        public bool HasItemTable { get; set; }

        public decimal Confidence { get; set; }
    }

    public sealed class ExcelImportFieldAnalysis
    {
        public string FieldKey { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public string Value { get; set; } = string.Empty;

        public string WorksheetName { get; set; } = string.Empty;

        public int Row { get; set; }

        public int Column { get; set; }

        public decimal Confidence { get; set; }

        public string Source { get; set; } = string.Empty;
    }

    public sealed class ExcelImportItemTableAnalysis
    {
        public string WorksheetName { get; set; } = string.Empty;

        public int HeaderRow { get; set; }

        public int HeaderDepth { get; set; }

        public int DataStartRow { get; set; }

        public decimal Confidence { get; set; }

        public ExcelImportItemColumnAnalysis Columns { get; set; } = new();
    }

    public sealed class ExcelImportItemColumnAnalysis
    {
        public int PoNumberCol { get; set; }

        public int StyleNoCol { get; set; }

        public int StyleNameCol { get; set; }

        public int FabricCompositionCol { get; set; }

        public int StyleNameCNCol { get; set; }

        public int BrandCol { get; set; }

        public int HSCodeCol { get; set; }

        public int OriginCol { get; set; }

        public int QuantityCol { get; set; }

        public int UnitENCol { get; set; }

        public int UnitCNCol { get; set; }

        public int CartonsCol { get; set; }

        public int CtnUnitENCol { get; set; }

        public int LengthCol { get; set; }

        public int WidthCol { get; set; }

        public int HeightCol { get; set; }

        public int DimensionCol { get; set; }

        public int VolumeCol { get; set; }

        public int GWPerCtnCol { get; set; }

        public int GWTotalCol { get; set; }

        public int NWPerCtnCol { get; set; }

        public int NWTotalCol { get; set; }

        public int UnitPriceCol { get; set; }

        public int TotalPriceCol { get; set; }
    }

    public sealed class ExcelImportAnalysisIssue
    {
        public string Severity { get; set; } = "Info";

        public string Code { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string FieldKey { get; set; } = string.Empty;
    }
}
