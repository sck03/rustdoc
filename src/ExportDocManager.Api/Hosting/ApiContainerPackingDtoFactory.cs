using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Api.Hosting
{
    public static class ApiContainerPackingDtoFactory
    {
        public const string StoragePolicy =
            "装柜/装箱分析只处理请求中的内存数据并返回分析结果；不会写入数据库、文件、系统 C 盘或 App_Data，WinForms/Tauri/Web 可在展示层自行选择是否保存。";

        public static ContainerPackingRequest ToRequest(ApiContainerPackingAnalyzeRequest request)
        {
            ArgumentNullException.ThrowIfNull(request);

            var container = request.Container ?? new ApiContainerDimensionsDto();
            var rules = request.Rules ?? new ApiContainerPackingRulesDto();
            var cargoItems = (request.CargoItems ?? new List<ApiContainerPackingCargoInputDto>())
                .Where(item => item != null)
                .Select(ToCargoInput)
                .ToList();

            return new ContainerPackingRequest(
                new ContainerDimensions(
                    Math.Max(container.Length, 0),
                    Math.Max(container.Width, 0),
                    Math.Max(container.Height, 0),
                    Math.Max(container.Volume, 0m),
                    Math.Max(container.MaxWeight, 0m)),
                cargoItems,
                new ContainerPackingRules(
                    rules.AllowRotation,
                    rules.UsePalletConstraints,
                    Math.Max(rules.DefaultPalletLength, 1),
                    Math.Max(rules.DefaultPalletWidth, 1),
                    Math.Max(rules.DefaultPalletHeight, 0),
                    Math.Max(rules.DefaultPalletWeight, 0m),
                    rules.EnforceCenterOfGravity,
                    Math.Max(rules.CenterOfGravityTolerancePercent, 0m),
                    Math.Clamp(rules.MinimumSupportAreaPercent, 0m, 100m),
                    rules.RequireSameFootprintStacking));
        }

        public static ApiContainerPackingAnalyzeResponse FromAnalysis(ContainerPackingAnalysis analysis)
        {
            analysis ??= new ContainerPackingAnalysis(
                Array.Empty<PackedCargoItem>(),
                0,
                0,
                0,
                0,
                0,
                0m,
                0m,
                0m,
                0m,
                0m,
                0m,
                0,
                0,
                0m,
                0m,
                0m,
                0m,
                true);

            return new ApiContainerPackingAnalyzeResponse(
                new ApiContainerPackingAnalysisDto
                {
                    PackedItems = (analysis.PackedItems ?? Array.Empty<PackedCargoItem>())
                        .Select(FromPackedCargoItem)
                        .ToList(),
                    TotalPackages = analysis.TotalPackages,
                    PackedPackages = analysis.PackedPackages,
                    UnpackedPackages = analysis.UnpackedPackages,
                    TotalPallets = analysis.TotalPallets,
                    PackedPallets = analysis.PackedPallets,
                    TotalVolume = analysis.TotalVolume,
                    TotalWeight = analysis.TotalWeight,
                    PackedVolume = analysis.PackedVolume,
                    PackedWeight = analysis.PackedWeight,
                    VolumeUtilizationPercent = analysis.VolumeUtilizationPercent,
                    WeightUtilizationPercent = analysis.WeightUtilizationPercent,
                    ContainersNeededByVolume = analysis.ContainersNeededByVolume,
                    ContainersNeededByWeight = analysis.ContainersNeededByWeight,
                    EstimatedContainerCount = analysis.EstimatedContainerCount,
                    CenterOfGravityX = analysis.CenterOfGravityX,
                    CenterOfGravityY = analysis.CenterOfGravityY,
                    CenterOfGravityLengthDeviationPercent = analysis.CenterOfGravityLengthDeviationPercent,
                    CenterOfGravityWidthDeviationPercent = analysis.CenterOfGravityWidthDeviationPercent,
                    IsCenterOfGravityWithinTolerance = analysis.IsCenterOfGravityWithinTolerance
                },
                StoragePolicy);
        }

        private static ContainerPackingCargoInput ToCargoInput(ApiContainerPackingCargoInputDto item)
        {
            return new ContainerPackingCargoInput(
                string.IsNullOrWhiteSpace(item.Name) ? "货物" : item.Name.Trim(),
                Math.Max(item.Length, 0m),
                Math.Max(item.Width, 0m),
                Math.Max(item.Height, 0m),
                Math.Max(item.Weight, 0m),
                Math.Max(item.Quantity, 0),
                ContainerPackingColor.FromArgb(item.ColorArgb),
                item.UsePallet,
                Math.Max(item.UnitsPerPallet, 1),
                Math.Max(item.MaxTopLoadWeight, 0m),
                ParseZone(item.PreferredZone),
                Math.Max(item.LoadSequence, 1),
                item.PriorityGroup?.Trim() ?? string.Empty);
        }

        private static ApiPackedCargoItemDto FromPackedCargoItem(PackedCargoItem item)
        {
            return new ApiPackedCargoItemDto
            {
                X = item.X,
                Y = item.Y,
                Width = item.Width,
                Height = item.Height,
                BaseHeight = item.BaseHeight,
                OccupiedHeight = item.OccupiedHeight,
                TopHeight = item.TopHeight,
                ColorArgb = item.Color.ToArgb(),
                UnitsRepresented = item.UnitsRepresented,
                StackCount = item.StackCount,
                LoadCount = item.LoadCount,
                DisplayText = item.DisplayText ?? string.Empty,
                DetailText = item.DetailText ?? string.Empty,
                IsRotated = item.IsRotated,
                IsPalletized = item.IsPalletized,
                Name = item.Name ?? string.Empty,
                TotalWeight = item.TotalWeight,
                PriorityGroup = item.PriorityGroup ?? string.Empty,
                PreferredZone = item.PreferredZone.ToString()
            };
        }

        private static ContainerCargoZone ParseZone(string zone)
        {
            return Enum.TryParse<ContainerCargoZone>(zone, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
                ? parsed
                : ContainerCargoZone.Auto;
        }
    }
}
