using System.Threading;
using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Errors;

namespace ExportDocManager.Services.Tools
{
    public sealed partial class ContainerPackingEngine : IContainerPackingEngine
    {
        public ContainerPackingAnalysis Analyze(ContainerPackingRequest request, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ContainerPackingResourcePolicy.Validate(request);

            using var executionLease = ContainerPackingExecutionGate.Enter(cancellationToken);
            return AnalyzeWithTimeout(request, cancellationToken);
        }

        public async Task<ContainerPackingAnalysis> AnalyzeAsync(
            ContainerPackingRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            ContainerPackingResourcePolicy.Validate(request);

            using var executionLease = await ContainerPackingExecutionGate
                .EnterAsync(cancellationToken)
                .ConfigureAwait(false);
            return AnalyzeWithTimeout(request, cancellationToken);
        }

        private static ContainerPackingAnalysis AnalyzeWithTimeout(
            ContainerPackingRequest request,
            CancellationToken cancellationToken)
        {
            using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            operationCancellation.CancelAfter(ContainerPackingExecutionGate.ResolveOperationTimeout());
            try
            {
                return AnalyzeCore(request, operationCancellation.Token);
            }
            catch (OperationCanceledException ex) when (
                !cancellationToken.IsCancellationRequested &&
                operationCancellation.IsCancellationRequested)
            {
                throw new ServiceTimeoutException(
                    "装箱分析超过允许的计算时间，请减少货物行数、使用托盘约束或拆分方案。",
                    ex);
            }
        }

        private static ContainerPackingAnalysis AnalyzeCore(
            ContainerPackingRequest request,
            CancellationToken cancellationToken)
        {
            var container = request.Container;
            var rules = request.Rules;
            var itemStates = BuildItemStates(request)
                .OrderBy(item => item.LoadSequence)
                .ThenBy(item => item.PreferredZone == ContainerCargoZone.Auto ? 1 : 0)
                .ThenBy(item => item.ZoneOrder)
                .ThenBy(item => item.PriorityGroup, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(item => item.BaseFootprint)
                .ThenByDescending(item => item.Height)
                .ThenByDescending(item => item.WeightPerLoad)
                .ToList();

            int totalPackages = itemStates.Sum(item => item.TotalUnitsRepresented);
            int totalPallets = itemStates.Sum(item => item.IsPalletized ? item.TotalLoadCount : 0);
            decimal totalVolume = itemStates.Sum(item => item.TotalVolume);
            decimal totalWeight = itemStates.Sum(item => item.TotalWeight);

            if (container.Length <= 0 ||
                container.Width <= 0 ||
                container.Height <= 0 ||
                itemStates.Count == 0)
            {
                return CreateAnalysis(
                    packedItems: [],
                    placedUnits: [],
                    totalPackages: totalPackages,
                    packedPackages: 0,
                    totalPallets: totalPallets,
                    packedPallets: 0,
                    totalVolume: totalVolume,
                    totalWeight: totalWeight,
                    container: container,
                    rules: rules);
            }

            var planners = CreateZonePlanners(container);
            var stacks = new List<PackingStack>();
            var placedUnits = new List<PlacedLoadUnit>();
            decimal remainingWeightCapacity = Math.Max(container.MaxWeight, 0);
            int packedPackages = 0;
            int packedPallets = 0;

            foreach (var itemState in itemStates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                while (itemState.RemainingLoadCount > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var candidate = SelectBestPlacement(
                        itemState,
                        stacks,
                        planners,
                        placedUnits,
                        container,
                        rules,
                        remainingWeightCapacity,
                        cancellationToken);

                    if (candidate == null)
                    {
                        break;
                    }

                    CommitPlacement(candidate.Value, stacks, planners, placedUnits);
                    remainingWeightCapacity -= candidate.Value.Placement.TotalWeight;
                    itemState.RemainingLoadCount--;
                    packedPackages += candidate.Value.Placement.UnitsRepresented;
                    if (candidate.Value.Placement.IsPalletized)
                    {
                        packedPallets++;
                    }
                }
            }

            return CreateAnalysis(
                packedItems: BuildPackedItems(stacks),
                placedUnits: placedUnits,
                totalPackages: totalPackages,
                packedPackages: packedPackages,
                totalPallets: totalPallets,
                packedPallets: packedPallets,
                totalVolume: totalVolume,
                totalWeight: totalWeight,
                container: container,
                rules: rules);
        }
    }
}
