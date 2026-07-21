using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Api.Hosting
{
    public sealed class ApiContainerPackingAnalyzeRequest
    {
        public ApiContainerDimensionsDto Container { get; set; } = new();

        public List<ApiContainerPackingCargoInputDto> CargoItems { get; set; } = new();

        public ApiContainerPackingRulesDto Rules { get; set; } = new();
    }

    public sealed class ApiContainerDimensionsDto
    {
        public int Length { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public decimal Volume { get; set; }

        public decimal MaxWeight { get; set; }
    }

    public sealed class ApiContainerPackingCargoInputDto
    {
        public string Name { get; set; } = string.Empty;

        public decimal Length { get; set; }

        public decimal Width { get; set; }

        public decimal Height { get; set; }

        public decimal Weight { get; set; }

        public int Quantity { get; set; }

        public int ColorArgb { get; set; } = unchecked((int)0xFF4287F5);

        public bool UsePallet { get; set; }

        public int UnitsPerPallet { get; set; } = 1;

        public decimal MaxTopLoadWeight { get; set; }

        public string PreferredZone { get; set; } = nameof(ContainerCargoZone.Auto);

        public int LoadSequence { get; set; } = 1;

        public string PriorityGroup { get; set; } = string.Empty;
    }

    public sealed class ApiContainerPackingRulesDto
    {
        public bool AllowRotation { get; set; } = true;

        public bool UsePalletConstraints { get; set; }

        public int DefaultPalletLength { get; set; } = 120;

        public int DefaultPalletWidth { get; set; } = 100;

        public int DefaultPalletHeight { get; set; } = 15;

        public decimal DefaultPalletWeight { get; set; } = 25m;

        public bool EnforceCenterOfGravity { get; set; }

        public decimal CenterOfGravityTolerancePercent { get; set; } = 20m;

        public decimal MinimumSupportAreaPercent { get; set; } = 100m;

        public bool RequireSameFootprintStacking { get; set; }
    }

    public sealed class ApiContainerPackingAnalyzeResponse
    {
        public ApiContainerPackingAnalyzeResponse(
            ApiContainerPackingAnalysisDto analysis,
            string storagePolicy)
        {
            Analysis = analysis;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public ApiContainerPackingAnalysisDto Analysis { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiContainerPackingProjectListResponse
    {
        public ApiContainerPackingProjectListResponse(
            List<ApiContainerPackingProjectSummaryDto> projects,
            string storagePolicy)
        {
            Projects = projects ?? new List<ApiContainerPackingProjectSummaryDto>();
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public List<ApiContainerPackingProjectSummaryDto> Projects { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiContainerPackingProjectResponse
    {
        public ApiContainerPackingProjectResponse(
            ApiContainerPackingProjectDto project,
            string storagePolicy)
        {
            Project = project;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public ApiContainerPackingProjectDto Project { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiContainerPackingProjectSaveRequest
    {
        public int Id { get; set; }

        public int ExpectedVersion { get; set; }

        public string Name { get; set; } = string.Empty;

        public string ContainerType { get; set; } = string.Empty;

        public ApiContainerDimensionsDto Container { get; set; } = new();

        public ApiContainerPackingRulesDto Rules { get; set; } = new();

        public List<ApiContainerPackingCargoInputDto> CargoItems { get; set; } = new();
    }

    public sealed class ApiContainerPackingProjectSaveResponse
    {
        public ApiContainerPackingProjectSaveResponse(
            bool success,
            int id,
            ApiContainerPackingProjectDto project,
            string message,
            string storagePolicy)
        {
            Success = success;
            Id = id;
            Project = project;
            Message = message ?? string.Empty;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public bool Success { get; }

        public int Id { get; }

        public ApiContainerPackingProjectDto Project { get; }

        public string Message { get; }

        public string StoragePolicy { get; }
    }

    public class ApiContainerPackingProjectSummaryDto
    {
        public int Id { get; set; }

        public int VersionNumber { get; set; }

        public string Name { get; set; } = string.Empty;

        public string ContainerType { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }
    }

    public sealed class ApiContainerPackingProjectDto : ApiContainerPackingProjectSummaryDto
    {
        public ApiContainerDimensionsDto Container { get; set; } = new();

        public ApiContainerPackingRulesDto Rules { get; set; } = new();

        public List<ApiContainerPackingCargoInputDto> CargoItems { get; set; } = new();
    }

    public sealed class ApiContainerTypeListResponse
    {
        public ApiContainerTypeListResponse(
            List<ApiContainerTypeDto> containerTypes,
            string storagePolicy)
        {
            ContainerTypes = containerTypes ?? new List<ApiContainerTypeDto>();
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public List<ApiContainerTypeDto> ContainerTypes { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiContainerTypeSaveRequest
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Length { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public decimal MaxVolume { get; set; }

        public decimal MaxWeight { get; set; }
    }

    public sealed class ApiContainerTypeSaveResponse
    {
        public ApiContainerTypeSaveResponse(
            bool success,
            int id,
            ApiContainerTypeDto containerType,
            string message,
            string storagePolicy)
        {
            Success = success;
            Id = id;
            ContainerType = containerType;
            Message = message ?? string.Empty;
            StoragePolicy = storagePolicy ?? string.Empty;
        }

        public bool Success { get; }

        public int Id { get; }

        public ApiContainerTypeDto ContainerType { get; }

        public string Message { get; }

        public string StoragePolicy { get; }
    }

    public sealed class ApiContainerTypeDto
    {
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        public int Length { get; set; }

        public int Width { get; set; }

        public int Height { get; set; }

        public decimal MaxVolume { get; set; }

        public decimal MaxWeight { get; set; }

        public bool IsSystemDefault { get; set; }
    }

    public sealed class ApiContainerPackingAnalysisDto
    {
        public List<ApiPackedCargoItemDto> PackedItems { get; set; } = new();

        public int TotalPackages { get; set; }

        public int PackedPackages { get; set; }

        public int UnpackedPackages { get; set; }

        public int TotalPallets { get; set; }

        public int PackedPallets { get; set; }

        public decimal TotalVolume { get; set; }

        public decimal TotalWeight { get; set; }

        public decimal PackedVolume { get; set; }

        public decimal PackedWeight { get; set; }

        public decimal VolumeUtilizationPercent { get; set; }

        public decimal WeightUtilizationPercent { get; set; }

        public int ContainersNeededByVolume { get; set; }

        public int ContainersNeededByWeight { get; set; }

        public int EstimatedContainerCount { get; set; }

        public decimal CenterOfGravityX { get; set; }

        public decimal CenterOfGravityY { get; set; }

        public decimal CenterOfGravityLengthDeviationPercent { get; set; }

        public decimal CenterOfGravityWidthDeviationPercent { get; set; }

        public bool IsCenterOfGravityWithinTolerance { get; set; }
    }

    public sealed class ApiPackedCargoItemDto
    {
        public float X { get; set; }

        public float Y { get; set; }

        public float Width { get; set; }

        public float Height { get; set; }

        public float BaseHeight { get; set; }

        public float OccupiedHeight { get; set; }

        public float TopHeight { get; set; }

        public int ColorArgb { get; set; }

        public int UnitsRepresented { get; set; }

        public int StackCount { get; set; }

        public int LoadCount { get; set; }

        public string DisplayText { get; set; } = string.Empty;

        public string DetailText { get; set; } = string.Empty;

        public bool IsRotated { get; set; }

        public bool IsPalletized { get; set; }

        public string Name { get; set; } = string.Empty;

        public decimal TotalWeight { get; set; }

        public string PriorityGroup { get; set; } = string.Empty;

        public string PreferredZone { get; set; } = string.Empty;
    }
}
