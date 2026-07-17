using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Tools
{
    public sealed partial class ContainerPackingEngine
    {
        private static decimal AlignStackedCoordinate(decimal baseCoordinate, decimal supportingSize, decimal stackingSize)
        {
            return baseCoordinate + (supportingSize - stackingSize) / 2m;
        }

        private static bool FitsWithinContainer(PlacedLoadUnit placement, ContainerDimensions container)
        {
            return placement.X >= 0 &&
                   placement.Y >= 0 &&
                   placement.X + placement.Length <= container.Length &&
                   placement.Y + placement.Width <= container.Width &&
                   placement.BaseHeight >= 0 &&
                   placement.TopHeight <= container.Height;
        }

        private static bool CanStackWithinHeight(decimal baseHeight, decimal occupiedHeight, int containerHeight)
        {
            return baseHeight + occupiedHeight <= containerHeight;
        }

        private static bool CanSupportAdditionalTopWeight(PackingStack stack, decimal additionalWeight)
        {
            decimal weightAbove = additionalWeight;

            for (int index = stack.Layers.Count - 1; index >= 0; index--)
            {
                var layer = stack.Layers[index];
                if (layer.MaxTopLoadWeight > 0 && weightAbove > layer.MaxTopLoadWeight + 0.01m)
                {
                    return false;
                }

                weightAbove += layer.TotalWeight;
            }

            return true;
        }

        private static decimal CalculateSupportAreaPercent(
            decimal x,
            decimal y,
            decimal length,
            decimal width,
            decimal supportX,
            decimal supportY,
            decimal supportLength,
            decimal supportWidth)
        {
            decimal overlapLength = Math.Max(0m, Math.Min(x + length, supportX + supportLength) - Math.Max(x, supportX));
            decimal overlapWidth = Math.Max(0m, Math.Min(y + width, supportY + supportWidth) - Math.Max(y, supportY));
            decimal overlapArea = overlapLength * overlapWidth;
            decimal targetArea = length * width;

            if (targetArea <= 0)
            {
                return 0;
            }

            return overlapArea / targetArea * 100m;
        }

        private static CenterOfGravityMetrics CalculateCenterOfGravity(
            IReadOnlyCollection<PlacedLoadUnit> placements,
            ContainerDimensions container,
            ContainerPackingRules rules)
        {
            decimal totalWeight = placements.Sum(item => item.TotalWeight);
            decimal centerX = container.Length / 2m;
            decimal centerY = container.Width / 2m;

            if (totalWeight <= 0)
            {
                return new CenterOfGravityMetrics(centerX, centerY, 0, 0, true);
            }

            decimal momentX = placements.Sum(item => item.TotalWeight * (item.X + item.Length / 2m));
            decimal momentY = placements.Sum(item => item.TotalWeight * (item.Y + item.Width / 2m));
            decimal cgX = momentX / totalWeight;
            decimal cgY = momentY / totalWeight;
            decimal lengthDeviationPercent = centerX > 0 ? Math.Abs(cgX - centerX) / centerX * 100 : 0;
            decimal widthDeviationPercent = centerY > 0 ? Math.Abs(cgY - centerY) / centerY * 100 : 0;
            decimal tolerance = Math.Max(rules.CenterOfGravityTolerancePercent, 0);
            bool isWithinTolerance = lengthDeviationPercent <= tolerance && widthDeviationPercent <= tolerance;

            return new CenterOfGravityMetrics(cgX, cgY, lengthDeviationPercent, widthDeviationPercent, isWithinTolerance);
        }

        private static bool AreClose(decimal left, decimal right)
        {
            return Math.Abs(left - right) <= 0.05m;
        }
    }
}
