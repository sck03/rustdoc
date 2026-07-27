using ExportDocManager.Utils;

namespace ExportDocManager.Domain.Tests
{
    public class DateTimeValueHelperTests
    {
        [Fact]
        public void NormalizeBusinessDate_ShouldPreserveCalendarFieldsAndUseUtcKind()
        {
            var value = new DateTime(2026, 7, 27, 0, 0, 0, DateTimeKind.Unspecified);

            var normalized = DateTimeValueHelper.NormalizeBusinessDate(value);

            Assert.Equal(value.Ticks, normalized.Ticks);
            Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        }

        [Fact]
        public void NormalizeBusinessDate_ShouldNormalizeFallbackWithoutChangingItsDate()
        {
            var fallback = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Local);

            var normalized = DateTimeValueHelper.NormalizeBusinessDate(default, fallback);

            Assert.Equal(fallback.Ticks, normalized.Ticks);
            Assert.Equal(DateTimeKind.Utc, normalized.Kind);
        }

        [Fact]
        public void NormalizeUtcTimestamp_ShouldPreserveUnspecifiedFieldsAndConvertLocalInstant()
        {
            var unspecified = new DateTime(2026, 7, 27, 9, 30, 0, DateTimeKind.Unspecified);
            var local = new DateTime(2026, 7, 27, 9, 30, 0, DateTimeKind.Local);

            var normalizedUnspecified = DateTimeValueHelper.NormalizeUtcTimestamp(unspecified);
            var normalizedLocal = DateTimeValueHelper.NormalizeUtcTimestamp(local);

            Assert.Equal(unspecified.Ticks, normalizedUnspecified.Ticks);
            Assert.Equal(DateTimeKind.Utc, normalizedUnspecified.Kind);
            Assert.Equal(local.ToUniversalTime(), normalizedLocal);
            Assert.Equal(DateTimeKind.Utc, normalizedLocal.Kind);
        }
    }
}
