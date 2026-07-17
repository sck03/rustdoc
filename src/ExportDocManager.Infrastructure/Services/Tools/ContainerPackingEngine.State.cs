using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Tools
{
    public sealed partial class ContainerPackingEngine
    {
        private readonly record struct PlacementOrientation(decimal Length, decimal Width, bool IsRotated);

        private readonly record struct FloorSlotReservation(
            decimal X,
            decimal Y,
            decimal SliceLength,
            decimal Width,
            bool StartsNewSlice,
            string GroupKey);

        private readonly record struct PlacementCandidate(
            PlacedLoadUnit Placement,
            ContainerCargoZone Zone,
            bool IsNewFloorSlot,
            bool ExactFootprintMatch,
            decimal SupportAreaPercent,
            FloorSlotReservation? Reservation,
            int? StackIndex,
            PlacementScore Score);

        private readonly record struct PlacementScore(
            decimal Primary,
            decimal Secondary,
            decimal Tertiary,
            decimal Quaternary,
            decimal Quinary,
            decimal Senary,
            decimal Septenary,
            decimal Octonary) : IComparable<PlacementScore>
        {
            public static PlacementScore MaxValue { get; } = new(
                decimal.MaxValue,
                decimal.MaxValue,
                decimal.MaxValue,
                decimal.MaxValue,
                decimal.MaxValue,
                decimal.MaxValue,
                decimal.MaxValue,
                decimal.MaxValue);

            public int CompareTo(PlacementScore other)
            {
                int result = Primary.CompareTo(other.Primary);
                if (result != 0)
                {
                    return result;
                }

                result = Secondary.CompareTo(other.Secondary);
                if (result != 0)
                {
                    return result;
                }

                result = Tertiary.CompareTo(other.Tertiary);
                if (result != 0)
                {
                    return result;
                }

                result = Quaternary.CompareTo(other.Quaternary);
                if (result != 0)
                {
                    return result;
                }

                result = Quinary.CompareTo(other.Quinary);
                if (result != 0)
                {
                    return result;
                }

                result = Senary.CompareTo(other.Senary);
                if (result != 0)
                {
                    return result;
                }

                result = Septenary.CompareTo(other.Septenary);
                if (result != 0)
                {
                    return result;
                }

                return Octonary.CompareTo(other.Octonary);
            }
        }

        private readonly record struct CenterOfGravityMetrics(
            decimal X,
            decimal Y,
            decimal LengthDeviationPercent,
            decimal WidthDeviationPercent,
            bool IsWithinTolerance)
        {
            public decimal TotalDeviationPercent => LengthDeviationPercent + WidthDeviationPercent;
        }

        private readonly record struct PlacedLoadUnit(
            string Name,
            decimal X,
            decimal Y,
            decimal Length,
            decimal Width,
            decimal BaseHeight,
            decimal OccupiedHeight,
            decimal TotalWeight,
            decimal MaxTopLoadWeight,
            int UnitsRepresented,
            int LoadCount,
            ContainerPackingColor Color,
            bool IsRotated,
            bool IsPalletized,
            string PriorityGroup,
            string GroupKey,
            ContainerCargoZone Zone)
        {
            public decimal TopHeight => BaseHeight + OccupiedHeight;
        }

        private sealed class PackingStack
        {
            public PackingStack(ContainerCargoZone zone, string groupKey, List<PlacedLoadUnit> layers)
            {
                Zone = zone;
                GroupKey = groupKey ?? string.Empty;
                Layers = layers ?? [];
            }

            public ContainerCargoZone Zone { get; }

            public string GroupKey { get; }

            public List<PlacedLoadUnit> Layers { get; }

            public PlacedLoadUnit? TopLayer => Layers.Count == 0 ? null : Layers[^1];

            public decimal TotalHeight => TopLayer?.TopHeight ?? 0m;

            public decimal BaseX => Layers.Count == 0 ? 0m : Layers[0].X;

            public decimal BaseY => Layers.Count == 0 ? 0m : Layers[0].Y;

            public decimal BaseLength => Layers.Count == 0 ? 0m : Layers[0].Length;

            public decimal BaseWidth => Layers.Count == 0 ? 0m : Layers[0].Width;

            public int ZoneOrder => Zone switch
            {
                ContainerCargoZone.Auto => 0,
                ContainerCargoZone.Head => 0,
                ContainerCargoZone.Middle => 1,
                ContainerCargoZone.Door => 2,
                _ => 3
            };

            public bool OverlapsFloor(decimal x, decimal y, decimal length, decimal width)
            {
                return x < BaseX + BaseLength &&
                       x + length > BaseX &&
                       y < BaseY + BaseWidth &&
                       y + width > BaseY;
            }
        }
    }
}
