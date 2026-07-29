using System.ComponentModel;
using ExportDocManager.Models.DTOs;

namespace ExportDocManager.Application.Tests
{
    public class DtoContractTests
    {
        [Fact]
        public void InvoiceCloneOptions_ShouldKeepExistingDefaults()
        {
            var options = new InvoiceCloneOptions();

            Assert.True(options.CopyHeader);
            Assert.True(options.CopyItems);
            Assert.True(options.ResetDates);
            Assert.False(options.ClearAmounts);
        }

        [Fact]
        public void QueryResultRow_ShouldKeepDisplayNames()
        {
            var attribute = typeof(QueryResultRow)
                .GetProperty(nameof(QueryResultRow.InvoiceNo))?
                .GetCustomAttributes(typeof(DisplayNameAttribute), inherit: false)
                .OfType<DisplayNameAttribute>()
                .SingleOrDefault();

            Assert.NotNull(attribute);
            Assert.Equal("发票号", attribute.DisplayName);
        }
    }
}
