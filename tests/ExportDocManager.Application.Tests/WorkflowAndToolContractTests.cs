using ExportDocManager.Models.DTOs;
using ExportDocManager.Services.Tools;

namespace ExportDocManager.Application.Tests
{
    public class WorkflowAndToolContractTests
    {
        [Fact]
        public void OcrResult_ShouldInitializeLinesCollection()
        {
            var first = new OcrResult();
            var second = new OcrResult();

            first.Lines.Add(new OcrLine { Text = "INV001", X = 1, Y = 2, Width = 3, Height = 4 });

            Assert.Single(first.Lines);
            Assert.Empty(second.Lines);
            Assert.Equal("INV001", first.Lines[0].Text);
            Assert.Equal(4, first.Lines[0].Height);
        }

        [Fact]
        public void ShutdownMaintenanceResult_ShouldReportCloudSyncFailureFromMessage()
        {
            Assert.False(new ShutdownMaintenanceResult().CloudSyncFailed);

            var result = new ShutdownMaintenanceResult
            {
                CloudSyncErrorMessage = "上传失败"
            };

            Assert.True(result.CloudSyncFailed);
        }

        [Fact]
        public void InvoiceImportResult_ShouldKeepDefaultConflictAction()
        {
            var result = new InvoiceImportResult();

            Assert.False(result.Success);
            Assert.Null(result.InvoiceId);
            Assert.Equal(InvoiceImportConflictAction.Skip, result.ActionTaken);
        }
    }
}
