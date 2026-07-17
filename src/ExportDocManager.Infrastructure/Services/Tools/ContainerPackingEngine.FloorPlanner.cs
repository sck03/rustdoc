using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Tools
{
    public sealed partial class ContainerPackingEngine
    {
        private sealed class ZoneFloorPlanner
        {
            private readonly decimal _zoneStartX;
            private readonly decimal _zoneEndX;
            private readonly decimal _containerLength;
            private readonly decimal _containerWidth;
            private decimal _nextSliceX;
            private decimal _currentSliceLength;
            private decimal _currentSliceNextY;

            public ZoneFloorPlanner(
                ContainerCargoZone zone,
                decimal zoneStartX,
                decimal zoneEndX,
                decimal containerLength,
                decimal containerWidth)
            {
                Zone = zone;
                _zoneStartX = Math.Max(zoneStartX, 0m);
                _zoneEndX = Math.Max(zoneEndX, _zoneStartX);
                _containerLength = Math.Max(containerLength, 0m);
                _containerWidth = Math.Max(containerWidth, 0m);
                _nextSliceX = _zoneStartX;
            }

            public ContainerCargoZone Zone { get; }

            public FloorSlotReservation? TryPreviewSlot(
                decimal length,
                decimal width,
                string groupKey,
                IReadOnlyList<PackingStack> occupiedStacks)
            {
                if (length <= 0 || width <= 0 || width > _containerWidth)
                {
                    return null;
                }

                decimal nextSliceX = _nextSliceX;
                decimal currentSliceLength = _currentSliceLength;
                decimal currentSliceNextY = _currentSliceNextY;

                for (int attempt = 0; attempt < 128; attempt++)
                {
                    var reservation = BuildPreview(length, width, groupKey, nextSliceX, currentSliceLength, currentSliceNextY);
                    if (reservation == null)
                    {
                        return null;
                    }

                    var overlappingFloorStack = occupiedStacks
                        .FirstOrDefault(stack => stack.OverlapsFloor(reservation.Value.X, reservation.Value.Y, length, width));
                    if (overlappingFloorStack == null)
                    {
                        return reservation;
                    }

                    nextSliceX = Math.Max(overlappingFloorStack.BaseX + overlappingFloorStack.BaseLength, reservation.Value.X + reservation.Value.SliceLength);
                    currentSliceLength = 0m;
                    currentSliceNextY = 0m;
                }

                return null;
            }

            public void Commit(FloorSlotReservation reservation)
            {
                if (reservation.StartsNewSlice)
                {
                    _nextSliceX = reservation.X;
                    _currentSliceLength = reservation.SliceLength;
                    _currentSliceNextY = reservation.Y + reservation.Width;
                }
                else
                {
                    _currentSliceLength = Math.Max(_currentSliceLength, reservation.SliceLength);
                    _currentSliceNextY = reservation.Y + reservation.Width;
                }

            }

            private bool CanStartNewSlice(decimal sliceStartX, decimal sliceLength)
            {
                return sliceStartX >= _zoneStartX &&
                       sliceStartX < _zoneEndX &&
                       sliceStartX + sliceLength <= _containerLength;
            }

            private FloorSlotReservation? BuildPreview(
                decimal length,
                decimal width,
                string groupKey,
                decimal nextSliceX,
                decimal currentSliceLength,
                decimal currentSliceNextY)
            {
                if (currentSliceLength <= 0m)
                {
                    if (!CanStartNewSlice(nextSliceX, length))
                    {
                        return null;
                    }

                    return new FloorSlotReservation(nextSliceX, 0m, length, width, true, groupKey);
                }

                decimal expandedSliceLength = Math.Max(currentSliceLength, length);
                if (currentSliceNextY + width <= _containerWidth &&
                    CanStartNewSlice(nextSliceX, expandedSliceLength))
                {
                    return new FloorSlotReservation(nextSliceX, currentSliceNextY, expandedSliceLength, width, false, groupKey);
                }

                decimal newSliceX = nextSliceX + currentSliceLength;
                if (!CanStartNewSlice(newSliceX, length))
                {
                    return null;
                }

                return new FloorSlotReservation(newSliceX, 0m, length, width, true, groupKey);
            }
        }
    }
}
