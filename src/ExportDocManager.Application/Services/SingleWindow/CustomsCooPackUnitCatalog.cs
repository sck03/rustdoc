using ExportDocManager.ViewModels;

namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooPackUnitCatalog
    {
        public static IReadOnlyList<SelectionOption<string>> CommonOptions { get; } =
        [
            new("CTN", "CTN"),
            new("BOX", "BOX"),
            new("BAG", "BAG"),
            new("BALE", "BALE"),
            new("PALLET", "PALLET"),
            new("PACKAGES", "PACKAGES"),
            new("HANGING GARMENT", "HANGING GARMENT"),
            new("IN BULK", "IN BULK"),
            new("PCS IN NUDE", "PCS IN NUDE"),
            new("SETS IN NUDE", "SETS IN NUDE"),
            new("UNITS IN NUDE", "UNITS IN NUDE")
        ];
    }
}
