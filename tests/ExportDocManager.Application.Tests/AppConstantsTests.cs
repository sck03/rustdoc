using ExportDocManager.Services.Time;
using ExportDocManager.Utils;

namespace ExportDocManager.Application.Tests
{
    public class AppConstantsTests
    {
        [Fact]
        public void PaymentCatalog_ShouldKeepLegacyDefaultsAndOrder()
        {
            Assert.Equal("电汇", AppConstants.DefaultPaymentMethod);
            Assert.Equal(["支票", "电汇", "预付"], AppConstants.PaymentMethods);
            Assert.Contains(AppConstants.DefaultPaymentMethod, AppConstants.PaymentMethods);
        }

        [Fact]
        public void InvoiceReferenceCatalogs_ShouldKeepLegacyValuesAndOrder()
        {
            Assert.Equal(["T/T", "L/C", "D/P", "D/A", "O/A"], AppConstants.PaymentTerms);
            Assert.Equal(["BY SEA", "BY AIR", "BY TRAIN", "BY DHL", "BY FedEx"], AppConstants.TransportModes);
            Assert.Equal(["USD", "EUR", "CNY", "GBP", "JPY"], AppConstants.Currencies);
            Assert.Equal(
                ["一般贸易", "来料加工", "进料加工", "补偿贸易", "易货贸易", "寄售代销", "边境小额贸易", "加工贸易", "保税仓库", "出料加工"],
                AppConstants.SupervisionModes);
            Assert.Equal(["报关数据", "实际数据"], AppConstants.TradeTypes);
        }

        [Theory]
        [InlineData("2026-08-14T15:59:59Z", 14)]
        [InlineData("2026-08-14T16:00:00Z", 15)]
        public void BusinessClock_ShouldUseShanghaiDateAcrossUtcBoundary(string utc, int expectedDay)
        {
            var clock = new BusinessClock(new FixedTimeProvider(DateTimeOffset.Parse(utc)), BusinessClock.DefaultTimeZoneId);
            Assert.Equal(new DateOnly(2026, 8, expectedDay), clock.Today);
            Assert.Equal(TimeSpan.FromHours(8), clock.Now.Offset);
        }

        [Fact]
        public void BusinessClock_ShouldExposeNextShanghaiMidnightInUtc()
        {
            var clock = new BusinessClock(
                new FixedTimeProvider(DateTimeOffset.Parse("2026-06-01T15:30:00Z")),
                BusinessClock.DefaultTimeZoneId);

            Assert.Equal(new DateTimeOffset(2026, 6, 1, 16, 0, 0, TimeSpan.Zero), clock.TodayValidUntilUtc);
        }

        private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
        {
            public override DateTimeOffset GetUtcNow() => now;
        }
    }
}
