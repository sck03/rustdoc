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

        public ExcelImportItemTableAnalysis? ItemTable { get; set; }

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
        private readonly int[] _columns = new int[24];

        public int PoNumberCol { get => _columns[0]; set => _columns[0] = value; }
        public int StyleNoCol { get => _columns[1]; set => _columns[1] = value; }
        public int StyleNameCol { get => _columns[2]; set => _columns[2] = value; }
        public int FabricCompositionCol { get => _columns[3]; set => _columns[3] = value; }
        public int StyleNameCNCol { get => _columns[4]; set => _columns[4] = value; }
        public int BrandCol { get => _columns[5]; set => _columns[5] = value; }
        public int HSCodeCol { get => _columns[6]; set => _columns[6] = value; }
        public int OriginCol { get => _columns[7]; set => _columns[7] = value; }
        public int QuantityCol { get => _columns[8]; set => _columns[8] = value; }
        public int UnitENCol { get => _columns[9]; set => _columns[9] = value; }
        public int UnitCNCol { get => _columns[10]; set => _columns[10] = value; }
        public int CartonsCol { get => _columns[11]; set => _columns[11] = value; }
        public int CtnUnitENCol { get => _columns[12]; set => _columns[12] = value; }
        public int LengthCol { get => _columns[13]; set => _columns[13] = value; }
        public int WidthCol { get => _columns[14]; set => _columns[14] = value; }
        public int HeightCol { get => _columns[15]; set => _columns[15] = value; }
        public int DimensionCol { get => _columns[16]; set => _columns[16] = value; }
        public int VolumeCol { get => _columns[17]; set => _columns[17] = value; }
        public int GWPerCtnCol { get => _columns[18]; set => _columns[18] = value; }
        public int GWTotalCol { get => _columns[19]; set => _columns[19] = value; }
        public int NWPerCtnCol { get => _columns[20]; set => _columns[20] = value; }
        public int NWTotalCol { get => _columns[21]; set => _columns[21] = value; }
        public int UnitPriceCol { get => _columns[22]; set => _columns[22] = value; }
        public int TotalPriceCol { get => _columns[23]; set => _columns[23] = value; }

        public void ClearColumn(int column)
        {
            if (column <= 0) return;
            for (int index = 0; index < _columns.Length; index++)
            {
                if (_columns[index] == column) _columns[index] = 0;
            }
        }

        public void MergeMissingNonConflictingFrom(ExcelImportItemColumnAnalysis source)
        {
            ArgumentNullException.ThrowIfNull(source);
            for (int index = 0; index < _columns.Length; index++)
            {
                int candidate = source._columns[index];
                if (_columns[index] <= 0 && candidate > 0 && Array.IndexOf(_columns, candidate) < 0)
                {
                    _columns[index] = candidate;
                }
            }
        }
    }

    public sealed class ExcelImportAnalysisIssue
    {
        public string Severity { get; set; } = "Info";

        public string Code { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string FieldKey { get; set; } = string.Empty;
    }
}
