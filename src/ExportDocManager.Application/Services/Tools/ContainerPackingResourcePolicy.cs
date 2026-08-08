using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Tools;

/// <summary>
/// Keeps interactive container analysis within predictable CPU and memory bounds.
/// The limits describe current product capability rather than a persistence format.
/// </summary>
public static class ContainerPackingResourcePolicy
{
    public const int MaximumCargoRows = 200;
    public const int MaximumQuantityPerRow = 1_000_000;
    public const int MaximumTotalPackages = 1_000_000;
    public const int MaximumPlacementUnits = 5_000;
    public const decimal MaximumDimensionCentimeters = 100_000m;
    public const decimal MaximumWeightKilograms = 1_000_000_000m;

    public static void Validate(ContainerPackingRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(request.CargoItems);
        ArgumentNullException.ThrowIfNull(request.Rules);

        ValidateContainer(request.Container);
        if (request.CargoItems.Count == 0)
        {
            throw new ServiceValidationException("至少需要一行货物。");
        }
        if (request.CargoItems.Count > MaximumCargoRows)
        {
            throw new ServiceValidationException($"一次装箱分析最多允许 {MaximumCargoRows} 行货物。");
        }

        long totalPackages = 0;
        long totalPlacementUnits = 0;
        for (int index = 0; index < request.CargoItems.Count; index++)
        {
            ContainerPackingCargoInput item = request.CargoItems[index]
                ?? throw new ServiceValidationException($"第 {index + 1} 行货物不能为空。");
            ValidateCargo(item, index + 1);

            totalPackages += item.Quantity;
            if (totalPackages > MaximumTotalPackages)
            {
                throw new ServiceValidationException(
                    $"一次装箱分析的货物总件数不能超过 {MaximumTotalPackages:N0} 件。");
            }

            totalPlacementUnits += CalculatePlacementUnits(item, request.Rules);
            if (totalPlacementUnits > MaximumPlacementUnits)
            {
                throw new ServiceValidationException(
                    $"当前货物需要模拟的装载单元超过 {MaximumPlacementUnits:N0} 个。" +
                    "请使用托盘约束、合并同规格货物或拆分方案后重试。");
            }
        }

        ValidateRules(request.Rules);
    }

    public static void ValidateContainer(ContainerDimensions container)
    {
        ValidatePositiveDimension(container.Length, "集装箱长度");
        ValidatePositiveDimension(container.Width, "集装箱宽度");
        ValidatePositiveDimension(container.Height, "集装箱高度");
        ValidateNonNegativeBounded(container.Volume, MaximumWeightKilograms, "集装箱体积");
        ValidateNonNegativeBounded(container.MaxWeight, MaximumWeightKilograms, "集装箱最大载重");
    }

    private static void ValidateCargo(ContainerPackingCargoInput item, int rowNumber)
    {
        if (item.Quantity is <= 0 or > MaximumQuantityPerRow)
        {
            throw new ServiceValidationException(
                $"第 {rowNumber} 行货物数量必须在 1 到 {MaximumQuantityPerRow:N0} 之间。");
        }

        ValidatePositiveDimension(item.Length, $"第 {rowNumber} 行货物长度");
        ValidatePositiveDimension(item.Width, $"第 {rowNumber} 行货物宽度");
        ValidatePositiveDimension(item.Height, $"第 {rowNumber} 行货物高度");
        ValidateNonNegativeBounded(item.Weight, MaximumWeightKilograms, $"第 {rowNumber} 行单件重量");
        ValidateNonNegativeBounded(
            item.MaxTopLoadWeight,
            MaximumWeightKilograms,
            $"第 {rowNumber} 行最大顶部承重");
        if (item.UnitsPerPallet is <= 0 or > MaximumQuantityPerRow)
        {
            throw new ServiceValidationException(
                $"第 {rowNumber} 行每托数量必须在 1 到 {MaximumQuantityPerRow:N0} 之间。");
        }
    }

    private static void ValidateRules(ContainerPackingRules rules)
    {
        ValidatePositiveDimension(rules.DefaultPalletLength, "默认托盘长度");
        ValidatePositiveDimension(rules.DefaultPalletWidth, "默认托盘宽度");
        ValidateNonNegativeBounded(rules.DefaultPalletHeight, MaximumDimensionCentimeters, "默认托盘高度");
        ValidateNonNegativeBounded(rules.DefaultPalletWeight, MaximumWeightKilograms, "默认托盘重量");
        if (rules.CenterOfGravityTolerancePercent is < 0m or > 100m)
        {
            throw new ServiceValidationException("重心容差必须在 0% 到 100% 之间。");
        }
        if (rules.MinimumSupportAreaPercent is < 0m or > 100m)
        {
            throw new ServiceValidationException("最小支撑面积必须在 0% 到 100% 之间。");
        }
    }

    private static long CalculatePlacementUnits(
        ContainerPackingCargoInput item,
        ContainerPackingRules rules)
    {
        if (!rules.UsePalletConstraints || !item.UsePallet)
        {
            return item.Quantity;
        }

        int unitsPerPallet = Math.Max(item.UnitsPerPallet, 1);
        return item.Quantity / unitsPerPallet + (item.Quantity % unitsPerPallet == 0 ? 0 : 1);
    }

    private static void ValidatePositiveDimension(decimal value, string label)
    {
        if (value <= 0m || value > MaximumDimensionCentimeters)
        {
            throw new ServiceValidationException(
                $"{label}必须大于 0 且不能超过 {MaximumDimensionCentimeters:N0} 厘米。");
        }
    }

    private static void ValidateNonNegativeBounded(decimal value, decimal maximum, string label)
    {
        if (value < 0m || value > maximum)
        {
            throw new ServiceValidationException($"{label}必须在 0 到 {maximum:N0} 之间。");
        }
    }
}
