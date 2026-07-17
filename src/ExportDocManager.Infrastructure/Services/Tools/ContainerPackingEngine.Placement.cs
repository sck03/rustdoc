using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Services.Tools
{
    public sealed partial class ContainerPackingEngine
    {
        private static IReadOnlyDictionary<ContainerCargoZone, ZoneFloorPlanner> CreateZonePlanners(ContainerDimensions container)
        {
            decimal segmentLength = container.Length / 3m;
            decimal middleStart = segmentLength;
            decimal doorStart = segmentLength * 2m;

            return new Dictionary<ContainerCargoZone, ZoneFloorPlanner>
            {
                [ContainerCargoZone.Auto] = new(ContainerCargoZone.Auto, 0m, container.Length, container.Length, container.Width),
                [ContainerCargoZone.Head] = new(ContainerCargoZone.Head, 0m, segmentLength, container.Length, container.Width),
                [ContainerCargoZone.Middle] = new(ContainerCargoZone.Middle, middleStart, doorStart, container.Length, container.Width),
                [ContainerCargoZone.Door] = new(ContainerCargoZone.Door, doorStart, container.Length, container.Length, container.Width)
            };
        }

        private static PlacementCandidate? SelectBestPlacement(
            PackingItemState itemState,
            IReadOnlyList<PackingStack> stacks,
            IReadOnlyDictionary<ContainerCargoZone, ZoneFloorPlanner> planners,
            IReadOnlyList<PlacedLoadUnit> placedUnits,
            ContainerDimensions container,
            ContainerPackingRules rules,
            decimal remainingWeightCapacity)
        {
            if (itemState.Height <= 0 || itemState.WeightPerLoad > remainingWeightCapacity)
            {
                return null;
            }

            PlacementCandidate? bestCandidate = null;
            foreach (var orientation in GetOrientations(itemState))
            {
                foreach (var floorCandidate in GetFloorCandidates(itemState, orientation, planners, stacks, container, rules.EnforceCenterOfGravity))
                {
                    bestCandidate = TryUpdateBestCandidate(
                        bestCandidate,
                        floorCandidate,
                        placedUnits,
                        container,
                        rules);
                }

                foreach (var stackCandidate in GetStackCandidates(itemState, orientation, stacks, container, rules))
                {
                    bestCandidate = TryUpdateBestCandidate(
                        bestCandidate,
                        stackCandidate,
                        placedUnits,
                        container,
                        rules);
                }
            }

            return bestCandidate;
        }

        private static PlacementCandidate? TryUpdateBestCandidate(
            PlacementCandidate? bestCandidate,
            PlacementCandidate candidate,
            IReadOnlyList<PlacedLoadUnit> placedUnits,
            ContainerDimensions container,
            ContainerPackingRules rules)
        {
            var centerMetrics = CalculateCenterOfGravity(
                placedUnits.Count == 0
                    ? [candidate.Placement]
                    : placedUnits.Concat([candidate.Placement]).ToList(),
                container,
                rules);

            if (rules.EnforceCenterOfGravity && !centerMetrics.IsWithinTolerance)
            {
                return bestCandidate;
            }

            var scoredCandidate = candidate with
            {
                Score = BuildPlacementScore(candidate, centerMetrics, rules)
            };

            if (bestCandidate == null || scoredCandidate.Score.CompareTo(bestCandidate.Value.Score) < 0)
            {
                return scoredCandidate;
            }

            return bestCandidate;
        }

        private static IEnumerable<PlacementOrientation> GetOrientations(PackingItemState itemState)
        {
            yield return new PlacementOrientation(itemState.BaseLength, itemState.BaseWidth, false);

            if (!itemState.CanRotate || itemState.BaseLength == itemState.BaseWidth)
            {
                yield break;
            }

            yield return new PlacementOrientation(itemState.BaseWidth, itemState.BaseLength, true);
        }

        private static IEnumerable<PlacementCandidate> GetFloorCandidates(
            PackingItemState itemState,
            PlacementOrientation orientation,
            IReadOnlyDictionary<ContainerCargoZone, ZoneFloorPlanner> planners,
            IReadOnlyList<PackingStack> stacks,
            ContainerDimensions container,
            bool allowCenteredFallback)
        {
            foreach (var zone in GetEligibleZones(itemState.PreferredZone))
            {
                if (!planners.TryGetValue(zone, out var planner))
                {
                    continue;
                }

                var reservation = planner.TryPreviewSlot(
                    orientation.Length,
                    orientation.Width,
                    itemState.GroupKey,
                    stacks);

                if (reservation == null)
                {
                    continue;
                }

                var placement = CreatePlacedLoadUnit(
                    itemState,
                    orientation,
                    reservation.Value.X,
                    reservation.Value.Y,
                    baseHeight: 0m,
                    zone);

                if (!FitsWithinContainer(placement, container))
                {
                    continue;
                }

                foreach (var variant in BuildFloorPlacementVariants(placement, reservation.Value, container, zone, allowCenteredFallback))
                {
                    yield return new PlacementCandidate(
                        variant.Placement,
                        zone,
                        IsNewFloorSlot: true,
                        ExactFootprintMatch: true,
                        SupportAreaPercent: 100m,
                        variant.Reservation,
                        StackIndex: null,
                        Score: PlacementScore.MaxValue);
                }
            }
        }

        private static IEnumerable<PlacementCandidate> GetStackCandidates(
            PackingItemState itemState,
            PlacementOrientation orientation,
            IReadOnlyList<PackingStack> stacks,
            ContainerDimensions container,
            ContainerPackingRules rules)
        {
            for (int index = 0; index < stacks.Count; index++)
            {
                var stack = stacks[index];
                if (!string.Equals(stack.GroupKey, itemState.GroupKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (itemState.PreferredZone != ContainerCargoZone.Auto && stack.Zone != itemState.PreferredZone)
                {
                    continue;
                }

                var topLayer = stack.TopLayer;
                if (topLayer == null)
                {
                    continue;
                }

                decimal x = AlignStackedCoordinate(topLayer.Value.X, topLayer.Value.Length, orientation.Length);
                decimal y = AlignStackedCoordinate(topLayer.Value.Y, topLayer.Value.Width, orientation.Width);
                var placement = CreatePlacedLoadUnit(
                    itemState,
                    orientation,
                    x,
                    y,
                    stack.TotalHeight,
                    stack.Zone);

                if (!FitsWithinContainer(placement, container))
                {
                    continue;
                }

                decimal supportAreaPercent = CalculateSupportAreaPercent(
                    placement.X,
                    placement.Y,
                    placement.Length,
                    placement.Width,
                    topLayer.Value.X,
                    topLayer.Value.Y,
                    topLayer.Value.Length,
                    topLayer.Value.Width);

                bool exactFootprintMatch =
                    AreClose(placement.Length, topLayer.Value.Length) &&
                    AreClose(placement.Width, topLayer.Value.Width);

                if (rules.RequireSameFootprintStacking && !exactFootprintMatch)
                {
                    continue;
                }

                if (supportAreaPercent + 0.01m < Math.Clamp(rules.MinimumSupportAreaPercent, 0m, 100m))
                {
                    continue;
                }

                if (!CanStackWithinHeight(stack.TotalHeight, placement.OccupiedHeight, container.Height))
                {
                    continue;
                }

                if (!CanSupportAdditionalTopWeight(stack, placement.TotalWeight))
                {
                    continue;
                }

                yield return new PlacementCandidate(
                    placement,
                    stack.Zone,
                    IsNewFloorSlot: false,
                    ExactFootprintMatch: exactFootprintMatch,
                    SupportAreaPercent: supportAreaPercent,
                    Reservation: null,
                    StackIndex: index,
                    Score: PlacementScore.MaxValue);
            }
        }

        private static void CommitPlacement(
            PlacementCandidate candidate,
            List<PackingStack> stacks,
            IReadOnlyDictionary<ContainerCargoZone, ZoneFloorPlanner> planners,
            List<PlacedLoadUnit> placedUnits)
        {
            if (candidate.IsNewFloorSlot)
            {
                planners[candidate.Zone].Commit(candidate.Reservation!.Value);
                stacks.Add(new PackingStack(candidate.Zone, candidate.Placement.GroupKey, [candidate.Placement]));
            }
            else if (candidate.StackIndex.HasValue)
            {
                stacks[candidate.StackIndex.Value].Layers.Add(candidate.Placement);
            }
            else
            {
                stacks.Add(new PackingStack(candidate.Zone, candidate.Placement.GroupKey, [candidate.Placement]));
            }

            placedUnits.Add(candidate.Placement);
        }

        private static PlacementScore BuildPlacementScore(
            PlacementCandidate candidate,
            CenterOfGravityMetrics centerMetrics,
            ContainerPackingRules rules)
        {
            decimal rotationPenalty = candidate.Placement.IsRotated ? 1m : 0m;
            decimal supportPenalty = candidate.ExactFootprintMatch ? 0m : 1m;
            // Favor vertical completion of a compatible stack before opening another floor slot.
            decimal layoutPhaseOrder = candidate.IsNewFloorSlot ? 1m : 0m;
            decimal zoneOrder = GetZoneOrder(candidate.Zone);

            if (rules.EnforceCenterOfGravity)
            {
                return new PlacementScore(
                    layoutPhaseOrder,
                    centerMetrics.TotalDeviationPercent,
                    candidate.Placement.BaseHeight,
                    candidate.Placement.X,
                    candidate.Placement.Y,
                    rotationPenalty,
                    supportPenalty,
                    -candidate.SupportAreaPercent);
            }

            return new PlacementScore(
                layoutPhaseOrder,
                zoneOrder,
                candidate.Placement.X,
                candidate.Placement.Y,
                candidate.Placement.BaseHeight,
                rotationPenalty,
                supportPenalty,
                -candidate.SupportAreaPercent);
        }

        private static decimal GetZoneOrder(ContainerCargoZone zone)
        {
            return zone switch
            {
                ContainerCargoZone.Auto => 0m,
                ContainerCargoZone.Head => 0m,
                ContainerCargoZone.Middle => 1m,
                ContainerCargoZone.Door => 2m,
                _ => 3m
            };
        }

        private static IEnumerable<ContainerCargoZone> GetEligibleZones(ContainerCargoZone preferredZone)
        {
            if (preferredZone != ContainerCargoZone.Auto)
            {
                yield return preferredZone;
                yield break;
            }

            yield return ContainerCargoZone.Auto;
        }

        private static PlacedLoadUnit CreatePlacedLoadUnit(
            PackingItemState itemState,
            PlacementOrientation orientation,
            decimal x,
            decimal y,
            decimal baseHeight,
            ContainerCargoZone zone)
        {
            return new PlacedLoadUnit(
                string.IsNullOrWhiteSpace(itemState.Name) ? "货物" : itemState.Name.Trim(),
                x,
                y,
                orientation.Length,
                orientation.Width,
                baseHeight,
                itemState.Height,
                itemState.WeightPerLoad,
                itemState.MaxTopLoadWeight,
                itemState.UnitsPerLoad,
                1,
                itemState.Color,
                orientation.IsRotated,
                itemState.IsPalletized,
                itemState.PriorityGroup,
                itemState.GroupKey,
                zone);
        }

        private static IEnumerable<(PlacedLoadUnit Placement, FloorSlotReservation Reservation)> BuildFloorPlacementVariants(
            PlacedLoadUnit placement,
            FloorSlotReservation reservation,
            ContainerDimensions container,
            ContainerCargoZone zone,
            bool allowCenteredFallback)
        {
            yield return (placement, reservation);

            if (!allowCenteredFallback)
            {
                yield break;
            }

            decimal centeredY = (container.Width - placement.Width) / 2m;
            if (centeredY > 0 && !AreClose(centeredY, placement.Y))
            {
                yield return (placement with { Y = centeredY }, reservation);
            }

            if (zone == ContainerCargoZone.Auto &&
                AreClose(reservation.X, 0m) &&
                AreClose(reservation.Y, 0m))
            {
                decimal centeredX = (container.Length - placement.Length) / 2m;
                if (centeredX > 0 && !AreClose(centeredX, placement.X))
                {
                    var centeredReservation = reservation with { X = centeredX };
                    var centeredPlacement = placement with { X = centeredX };
                    yield return (centeredPlacement, centeredReservation);

                    if (centeredY > 0 && !AreClose(centeredY, placement.Y))
                    {
                        yield return (
                            centeredPlacement with { Y = centeredY },
                            centeredReservation);
                    }
                }
            }
        }
    }
}
