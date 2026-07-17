using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Tools
{
    public sealed partial class ContainerPackingEngine
    {
        private static IReadOnlyList<PackedCargoItem> BuildPackedItems(IEnumerable<PackingStack> stacks)
        {
            var items = new List<PackedCargoItem>();

            foreach (var stack in stacks
                         .OrderBy(item => item.BaseX)
                         .ThenBy(item => item.BaseY)
                         .ThenBy(item => item.ZoneOrder))
            {
                PackedBlockAccumulator? current = null;

                foreach (var layer in stack.Layers.OrderBy(item => item.BaseHeight))
                {
                    if (current != null && current.Value.CanMerge(layer))
                    {
                        current = current.Value.Merge(layer);
                        continue;
                    }

                    if (current != null)
                    {
                        items.Add(current.Value.ToPackedCargoItem());
                    }

                    current = PackedBlockAccumulator.Create(layer);
                }

                if (current != null)
                {
                    items.Add(current.Value.ToPackedCargoItem());
                }
            }

            return items;
        }

        private static ContainerPackingAnalysis CreateAnalysis(
            IReadOnlyList<PackedCargoItem> packedItems,
            IReadOnlyCollection<PlacedLoadUnit> placedUnits,
            int totalPackages,
            int packedPackages,
            int totalPallets,
            int packedPallets,
            decimal totalVolume,
            decimal totalWeight,
            ContainerDimensions container,
            ContainerPackingRules rules)
        {
            decimal packedVolume = packedItems.Sum(item =>
                (decimal)item.Width * (decimal)item.Height * (decimal)item.OccupiedHeight / 1000000m);
            decimal packedWeight = packedItems.Sum(item => item.TotalWeight);
            decimal volumeUtilizationPercent = container.Volume > 0 ? packedVolume / container.Volume * 100 : 0;
            decimal weightUtilizationPercent = container.MaxWeight > 0 ? packedWeight / container.MaxWeight * 100 : 0;
            int containersNeededByVolume = container.Volume > 0 ? (int)Math.Ceiling(totalVolume / container.Volume) : 0;
            int containersNeededByWeight = container.MaxWeight > 0 ? (int)Math.Ceiling(totalWeight / container.MaxWeight) : 0;
            var cg = CalculateCenterOfGravity(placedUnits, container, rules);

            return new ContainerPackingAnalysis(
                packedItems,
                totalPackages,
                packedPackages,
                Math.Max(totalPackages - packedPackages, 0),
                totalPallets,
                packedPallets,
                totalVolume,
                totalWeight,
                packedVolume,
                packedWeight,
                volumeUtilizationPercent,
                weightUtilizationPercent,
                containersNeededByVolume,
                containersNeededByWeight,
                cg.X,
                cg.Y,
                cg.LengthDeviationPercent,
                cg.WidthDeviationPercent,
                cg.IsWithinTolerance);
        }

        private readonly record struct PackedBlockAccumulator(
            string Name,
            decimal X,
            decimal Y,
            decimal Width,
            decimal Height,
            decimal BaseHeight,
            decimal OccupiedHeight,
            ContainerPackingColor Color,
            int UnitsRepresented,
            int LoadCount,
            bool IsRotated,
            bool IsPalletized,
            decimal TotalWeight,
            string PriorityGroup,
            ContainerCargoZone PreferredZone)
        {
            public static PackedBlockAccumulator Create(PlacedLoadUnit layer)
            {
                return new PackedBlockAccumulator(
                    layer.Name,
                    layer.X,
                    layer.Y,
                    layer.Length,
                    layer.Width,
                    layer.BaseHeight,
                    layer.OccupiedHeight,
                    layer.Color,
                    layer.UnitsRepresented,
                    layer.LoadCount,
                    layer.IsRotated,
                    layer.IsPalletized,
                    layer.TotalWeight,
                    layer.PriorityGroup,
                    layer.Zone);
            }

            public bool CanMerge(PlacedLoadUnit layer)
            {
                return string.Equals(Name, layer.Name, StringComparison.Ordinal) &&
                       AreClose(X, layer.X) &&
                       AreClose(Y, layer.Y) &&
                       AreClose(Width, layer.Length) &&
                       AreClose(Height, layer.Width) &&
                       AreClose(BaseHeight + OccupiedHeight, layer.BaseHeight) &&
                       IsRotated == layer.IsRotated &&
                       IsPalletized == layer.IsPalletized &&
                       string.Equals(PriorityGroup, layer.PriorityGroup, StringComparison.Ordinal) &&
                       PreferredZone == layer.Zone;
            }

            public PackedBlockAccumulator Merge(PlacedLoadUnit layer)
            {
                return this with
                {
                    OccupiedHeight = OccupiedHeight + layer.OccupiedHeight,
                    UnitsRepresented = UnitsRepresented + layer.UnitsRepresented,
                    LoadCount = LoadCount + layer.LoadCount,
                    TotalWeight = TotalWeight + layer.TotalWeight
                };
            }

            public PackedCargoItem ToPackedCargoItem()
            {
                string displayText;
                string detailText;

                if (IsPalletized)
                {
                    displayText = $"{LoadCount}托";
                    detailText = $"{UnitsRepresented}箱";
                }
                else
                {
                    displayText = $"{LoadCount}箱";
                    detailText = $"高{OccupiedHeight:0}cm";
                }

                return new PackedCargoItem(
                    (float)X,
                    (float)Y,
                    (float)Width,
                    (float)Height,
                    (float)BaseHeight,
                    (float)OccupiedHeight,
                    Color,
                    UnitsRepresented,
                    LoadCount,
                    displayText,
                    detailText,
                    IsRotated,
                    IsPalletized,
                    Name,
                    TotalWeight,
                    PriorityGroup,
                    PreferredZone);
            }
        }
    }
}
