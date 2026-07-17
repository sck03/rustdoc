using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Infrastructure.Tests
{
    public class ContainerPackingEngineTests
    {
        [Fact]
        public void Analyze_ShouldPackSimpleCargoWithoutWinFormsDependencies()
        {
            var engine = new ContainerPackingEngine();
            var cargoColor = ContainerPackingColor.FromRgb(66, 135, 245);
            var request = new ContainerPackingRequest(
                new ContainerDimensions(1200, 235, 239, 67.4m, 28000m),
                [
                    new ContainerPackingCargoInput(
                        "样品箱",
                        Length: 100m,
                        Width: 80m,
                        Height: 60m,
                        Weight: 20m,
                        Quantity: 4,
                        Color: cargoColor,
                        UsePallet: false,
                        UnitsPerPallet: 1,
                        MaxTopLoadWeight: 0m,
                        PreferredZone: ContainerCargoZone.Auto,
                        LoadSequence: 1,
                        PriorityGroup: string.Empty)
                ],
                new ContainerPackingRules(
                    AllowRotation: true,
                    UsePalletConstraints: false,
                    DefaultPalletLength: 120,
                    DefaultPalletWidth: 100,
                    DefaultPalletHeight: 15,
                    DefaultPalletWeight: 25m,
                    EnforceCenterOfGravity: false,
                    CenterOfGravityTolerancePercent: 20m,
                    MinimumSupportAreaPercent: 70m,
                    RequireSameFootprintStacking: false));

            var analysis = engine.Analyze(request);

            Assert.Equal(4, analysis.TotalPackages);
            Assert.Equal(4, analysis.PackedPackages);
            Assert.Equal(0, analysis.UnpackedPackages);
            Assert.True(analysis.PackedVolume > 0);
            Assert.All(analysis.PackedItems, item => Assert.Equal(cargoColor, item.Color));
        }

        [Fact]
        public void Analyze_ShouldStackCompatibleCartonsBeforeOpeningNextFloorSlot()
        {
            var engine = new ContainerPackingEngine();
            var request = new ContainerPackingRequest(
                new ContainerDimensions(589, 235, 239, 28m, 21000m),
                [
                    new ContainerPackingCargoInput(
                        "纸箱",
                        Length: 60m,
                        Width: 40m,
                        Height: 40m,
                        Weight: 10m,
                        Quantity: 10,
                        Color: ContainerPackingColor.FromRgb(66, 135, 245),
                        UsePallet: false,
                        UnitsPerPallet: 1,
                        MaxTopLoadWeight: 0m,
                        PreferredZone: ContainerCargoZone.Auto,
                        LoadSequence: 1,
                        PriorityGroup: string.Empty)
                ],
                CreateDefaultRules());

            var analysis = engine.Analyze(request);

            Assert.Equal(10, analysis.PackedPackages);
            Assert.Equal(0, analysis.UnpackedPackages);
            Assert.Equal(2, analysis.PackedItems.Count);
            Assert.All(analysis.PackedItems, item =>
            {
                Assert.Equal(0f, item.X);
                Assert.Equal(5, item.LoadCount);
                Assert.Equal(200f, item.OccupiedHeight);
            });
            Assert.True(analysis.PackedItems.Max(item => item.X + item.Width) <= 60.01f);
        }

        [Fact]
        public void Analyze_ShouldKeepSequentialRowsCompactAcrossWidthBeforeAdvancingToDoor()
        {
            var engine = new ContainerPackingEngine();
            var cargoItems = Enumerable.Range(0, 9)
                .Select(index => new ContainerPackingCargoInput(
                    $"货物{index + 1}",
                    Length: 60m,
                    Width: 40m,
                    Height: 40m,
                    Weight: 10m,
                    Quantity: 10,
                    Color: ContainerPackingColor.FromRgb((byte)(66 + index), 135, 245),
                    UsePallet: false,
                    UnitsPerPallet: 1,
                    MaxTopLoadWeight: 0m,
                    PreferredZone: ContainerCargoZone.Auto,
                    LoadSequence: index + 1,
                    PriorityGroup: string.Empty))
                .ToList();
            var request = new ContainerPackingRequest(
                new ContainerDimensions(589, 235, 239, 28m, 21000m),
                cargoItems,
                CreateDefaultRules());

            var analysis = engine.Analyze(request);

            Assert.Equal(90, analysis.TotalPackages);
            Assert.Equal(90, analysis.PackedPackages);
            Assert.Equal(0, analysis.UnpackedPackages);
            Assert.Equal(18, analysis.PackedItems.Count);
            Assert.All(analysis.PackedItems, item => Assert.Equal(5, item.LoadCount));
            Assert.True(analysis.PackedItems.Max(item => item.X + item.Width) <= 240.01f);

            var occupiedXPositions = analysis.PackedItems
                .Select(item => item.X)
                .Distinct()
                .OrderBy(value => value)
                .ToList();

            Assert.Equal([0f, 60f, 120f, 180f], occupiedXPositions);
        }

        private static ContainerPackingRules CreateDefaultRules()
        {
            return new ContainerPackingRules(
                AllowRotation: true,
                UsePalletConstraints: false,
                DefaultPalletLength: 120,
                DefaultPalletWidth: 100,
                DefaultPalletHeight: 15,
                DefaultPalletWeight: 25m,
                EnforceCenterOfGravity: false,
                CenterOfGravityTolerancePercent: 20m,
                MinimumSupportAreaPercent: 100m,
                RequireSameFootprintStacking: false);
        }
    }
}
