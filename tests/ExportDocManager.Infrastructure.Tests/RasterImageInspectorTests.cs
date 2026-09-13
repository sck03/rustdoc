using ExportDocManager.Utils;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class RasterImageInspectorTests
{
    [Theory]
    [InlineData(15, 1000, false)]
    [InlineData(16, 191, false)]
    [InlineData(16, 192, true)]
    public void RasterBounds_ShouldApplyBeforeDecoding(int dimension, long pixels, bool accepted)
    {
        foreach (string format in new[] { "png", "jpeg", "gif", "webp" })
        {
            var info = RasterImageInspector.Inspect(RasterImageFixtures.Read(format), dimension, pixels);
            Assert.Equal(accepted, info != null);
            if (info != null) Assert.Equal((16, 12), (info.Width, info.Height));
        }
    }
}
