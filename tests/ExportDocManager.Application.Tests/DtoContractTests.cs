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
            Assert.True(options.ResetStatus);
            Assert.True(options.ResetDates);
            Assert.False(options.ClearAmounts);
        }

        [Fact]
        public void SharedDatabaseCapabilityProfile_ShouldAppendPlannedModules()
        {
            var profile = new SharedDatabaseCapabilityProfile
            {
                CurrentModeText = "当前是共享模式",
                PlannedModules = ["报表", "单一窗口"]
            };

            Assert.Equal("当前是共享模式；未来计划逐步支持共享数据库的模块：报表、单一窗口", profile.SummaryText);
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
