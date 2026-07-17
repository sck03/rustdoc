using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Tools
{
    public sealed partial class ContainerPackingEngine
    {
        private static IEnumerable<PackingItemState> BuildItemStates(ContainerPackingRequest request)
        {
            var rules = request.Rules;

            foreach (var cargo in request.CargoItems.Where(item =>
                         item.Quantity > 0 &&
                         item.Length > 0 &&
                         item.Width > 0 &&
                         item.Height > 0))
            {
                string normalizedGroup = NormalizePriorityGroup(cargo.PriorityGroup);
                int loadSequence = Math.Max(cargo.LoadSequence, 1);
                decimal maxTopLoadWeight = Math.Max(cargo.MaxTopLoadWeight, 0);
                var preferredZone = Enum.IsDefined(cargo.PreferredZone)
                    ? cargo.PreferredZone
                    : ContainerCargoZone.Auto;

                if (rules.UsePalletConstraints && cargo.UsePallet)
                {
                    int unitsPerPallet = Math.Max(cargo.UnitsPerPallet, 1);
                    int fullPalletCount = cargo.Quantity / unitsPerPallet;
                    int remainder = cargo.Quantity % unitsPerPallet;
                    decimal palletLength = Math.Max(cargo.Length, rules.DefaultPalletLength);
                    decimal palletWidth = Math.Max(cargo.Width, rules.DefaultPalletWidth);
                    decimal palletHeight = cargo.Height + Math.Max(rules.DefaultPalletHeight, 0);
                    decimal palletWeight = Math.Max(rules.DefaultPalletWeight, 0);

                    if (fullPalletCount > 0)
                    {
                        yield return new PackingItemState(
                            cargo.Name,
                            palletLength,
                            palletWidth,
                            palletHeight,
                            cargo.Weight * unitsPerPallet + palletWeight,
                            maxTopLoadWeight,
                            fullPalletCount,
                            unitsPerPallet,
                            cargo.Color,
                            rules.AllowRotation,
                            true,
                            preferredZone,
                            loadSequence,
                            normalizedGroup);
                    }

                    if (remainder > 0)
                    {
                        yield return new PackingItemState(
                            cargo.Name,
                            palletLength,
                            palletWidth,
                            palletHeight,
                            cargo.Weight * remainder + palletWeight,
                            maxTopLoadWeight,
                            1,
                            remainder,
                            cargo.Color,
                            rules.AllowRotation,
                            true,
                            preferredZone,
                            loadSequence,
                            normalizedGroup);
                    }

                    continue;
                }

                yield return new PackingItemState(
                    cargo.Name,
                    cargo.Length,
                    cargo.Width,
                    cargo.Height,
                    cargo.Weight,
                    maxTopLoadWeight,
                    cargo.Quantity,
                    1,
                    cargo.Color,
                    rules.AllowRotation,
                    false,
                    preferredZone,
                    loadSequence,
                    normalizedGroup);
            }
        }

        private static string NormalizePriorityGroup(string priorityGroup)
        {
            return string.IsNullOrWhiteSpace(priorityGroup) ? string.Empty : priorityGroup.Trim();
        }

        private sealed class PackingItemState
        {
            public PackingItemState(
                string name,
                decimal baseLength,
                decimal baseWidth,
                decimal height,
                decimal weightPerLoad,
                decimal maxTopLoadWeight,
                int remainingLoadCount,
                int unitsPerLoad,
                ContainerPackingColor color,
                bool canRotate,
                bool isPalletized,
                ContainerCargoZone preferredZone,
                int loadSequence,
                string priorityGroup)
            {
                Name = string.IsNullOrWhiteSpace(name) ? "货物" : name.Trim();
                BaseLength = baseLength;
                BaseWidth = baseWidth;
                Height = height;
                WeightPerLoad = weightPerLoad;
                MaxTopLoadWeight = maxTopLoadWeight;
                RemainingLoadCount = remainingLoadCount;
                TotalLoadCount = remainingLoadCount;
                UnitsPerLoad = unitsPerLoad;
                Color = color;
                CanRotate = canRotate;
                IsPalletized = isPalletized;
                PreferredZone = preferredZone;
                LoadSequence = loadSequence;
                PriorityGroup = priorityGroup ?? string.Empty;
                GroupKey = $"{LoadSequence}|{PriorityGroup}";
            }

            public string Name { get; }

            public decimal BaseLength { get; }

            public decimal BaseWidth { get; }

            public decimal BaseFootprint => BaseLength * BaseWidth;

            public decimal Height { get; }

            public decimal WeightPerLoad { get; }

            public decimal MaxTopLoadWeight { get; }

            public int RemainingLoadCount { get; set; }

            public int TotalLoadCount { get; }

            public int UnitsPerLoad { get; }

            public int TotalUnitsRepresented => TotalLoadCount * UnitsPerLoad;

            public decimal TotalVolume => BaseLength * BaseWidth * Height * TotalLoadCount / 1000000m;

            public decimal TotalWeight => WeightPerLoad * TotalLoadCount;

            public ContainerPackingColor Color { get; }

            public bool CanRotate { get; }

            public bool IsPalletized { get; }

            public ContainerCargoZone PreferredZone { get; }

            public int LoadSequence { get; }

            public string PriorityGroup { get; }

            public string GroupKey { get; }

            public int ZoneOrder => PreferredZone switch
            {
                ContainerCargoZone.Head => 0,
                ContainerCargoZone.Middle => 1,
                ContainerCargoZone.Door => 2,
                _ => 3
            };
        }
    }
}
