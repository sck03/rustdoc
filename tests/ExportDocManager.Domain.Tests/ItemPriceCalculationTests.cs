using ExportDocManager.Models.Entities;
using System.Globalization;

namespace ExportDocManager.Domain.Tests
{
    public class ItemPriceCalculationTests
    {
        [Fact]
        public void LineAmountDriven_ShouldKeepEnteredAmountAndDeriveFiveDecimalUnitPrice()
        {
            var item = new Item
            {
                Quantity = 3m,
                TotalPrice = 100m,
                UnitPrice = 0m,
                PriceCalculationMode = ItemPriceCalculationModeCatalog.LineAmountDriven
            };

            item.RecalculatePrice();

            Assert.Equal(ItemPriceCalculationModeCatalog.LineAmountDriven, item.PriceCalculationMode);
            Assert.Equal(33.33333m, item.UnitPrice);
            Assert.Equal(100.00m, item.TotalPrice);
        }

        [Fact]
        public void UnitPriceDriven_ShouldRecalculateAmountToCurrencyPrecision()
        {
            var item = new Item
            {
                Quantity = 3m,
                UnitPrice = 33.33333m,
                TotalPrice = 999m,
                PriceCalculationMode = ItemPriceCalculationModeCatalog.UnitPriceDriven
            };

            item.RecalculatePrice();

            Assert.Equal(ItemPriceCalculationModeCatalog.UnitPriceDriven, item.PriceCalculationMode);
            Assert.Equal(33.33333m, item.UnitPrice);
            Assert.Equal(100.00m, item.TotalPrice);
        }

        [Fact]
        public void LineAmountDriven_ShouldKeepAmountWhenQuantityChanges()
        {
            var item = new Item
            {
                Quantity = 3m,
                TotalPrice = 100m,
                PriceCalculationMode = ItemPriceCalculationModeCatalog.LineAmountDriven
            };

            item.Quantity = 7m;
            item.RecalculatePrice();

            Assert.Equal(14.28571m, item.UnitPrice);
            Assert.Equal(100.00m, item.TotalPrice);
        }

        [Theory]
        [InlineData("12", "12.00")]
        [InlineData("12.3", "12.30")]
        [InlineData("33.33333", "33.33333")]
        [InlineData("14.2857", "14.28570")]
        public void UnitPriceDisplay_ShouldUseTwoOrFiveDecimalPlaces(string value, string expected)
        {
            Assert.Equal(
                expected,
                ItemPricePrecisionPolicy.Format(decimal.Parse(value, CultureInfo.InvariantCulture)));
        }
    }
}
