using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Infrastructure.Tests
{
    public sealed class SingleWindowTrackingPolicyTests
    {
        [Fact]
        public void ReceiptTokenMatching_ShouldRequireBusinessTokenBoundaries()
        {
            Assert.True(ManualImportClientBridge.ContainsToken("Success_INV-1.xml", "INV-1"));
            Assert.True(ManualImportClientBridge.ContainsToken("batch/INV-1/receipt.xml", "INV-1"));
            Assert.False(ManualImportClientBridge.ContainsToken("Success_INV-10.xml", "INV-1"));
            Assert.False(ManualImportClientBridge.ContainsToken("XINV-1.xml", "INV-1"));
        }

        [Fact]
        public void SelectPrimaryReceipt_ShouldPreferTerminalStatusOverNewerPreliminaryStatus()
        {
            DateTime now = DateTime.UtcNow;
            var primary = SingleWindowTrackingService.SelectPrimaryReceipt(
            [
                CreateReceipt(SingleWindowReceiptBusinessStatus.Received, now),
                CreateReceipt(SingleWindowReceiptBusinessStatus.Approved, now.AddMinutes(-10))
            ]);

            Assert.Equal(SingleWindowReceiptBusinessStatus.Approved, primary.BusinessStatus);
        }

        [Fact]
        public void ShouldUpdateReceiptSummary_ShouldAllowHigherRankEvenWhenOfficialTimeIsOlder()
        {
            DateTime now = DateTime.UtcNow;
            var batch = new SwSubmissionBatch
            {
                LastBusinessStatus = SingleWindowReceiptBusinessStatus.Accepted.ToString(),
                LastReceiptAt = now
            };

            bool shouldUpdate = SingleWindowTrackingService.ShouldUpdateReceiptSummary(
                batch,
                CreateReceipt(SingleWindowReceiptBusinessStatus.Approved, now.AddMinutes(-5)));

            Assert.True(shouldUpdate);
        }

        [Fact]
        public void ShouldUpdateReceiptSummary_ShouldRejectLowerRankEvenWhenNewer()
        {
            DateTime now = DateTime.UtcNow;
            var batch = new SwSubmissionBatch
            {
                LastBusinessStatus = SingleWindowReceiptBusinessStatus.Approved.ToString(),
                LastReceiptAt = now
            };

            bool shouldUpdate = SingleWindowTrackingService.ShouldUpdateReceiptSummary(
                batch,
                CreateReceipt(SingleWindowReceiptBusinessStatus.Received, now.AddMinutes(5)));

            Assert.False(shouldUpdate);
        }

        [Fact]
        public void SubmitPackageBinding_ShouldRejectRebindingToAnotherCardProfile()
        {
            var batch = new SwSubmissionBatch
            {
                Status = SingleWindowBatchStatusCatalog.SubmitPackageImported,
                AssignedStationKey = "SWS-11111111111111111111111111111111",
                AssignedProfileKey = "SWP-22222222222222222222222222222222",
                AssignedCardIdentifier = "CARD-A"
            };

            var error = Assert.Throws<InvalidOperationException>(() =>
                SingleWindowTrackingService.EnsureSubmitPackageCanBindToStation(
                    batch,
                    batch.AssignedStationKey,
                    "SWP-33333333333333333333333333333333",
                    "CARD-B"));

            Assert.Contains("不能通过重复导入改绑", error.Message, StringComparison.Ordinal);
        }

        [Fact]
        public void SubmitPackageBinding_ShouldRejectStatusRollbackAfterDispatch()
        {
            var batch = new SwSubmissionBatch
            {
                Status = SingleWindowBatchStatusCatalog.QueuedToClient,
                AssignedStationKey = "SWS-11111111111111111111111111111111",
                AssignedProfileKey = "SWP-22222222222222222222222222222222",
                AssignedCardIdentifier = "CARD-A"
            };

            var error = Assert.Throws<InvalidOperationException>(() =>
                SingleWindowTrackingService.EnsureSubmitPackageCanBindToStation(
                    batch,
                    batch.AssignedStationKey,
                    batch.AssignedProfileKey,
                    batch.AssignedCardIdentifier));

            Assert.Contains("不能重复导入", error.Message, StringComparison.Ordinal);
        }

        private static SingleWindowReceiptParseResult CreateReceipt(
            SingleWindowReceiptBusinessStatus status,
            DateTime occurredAt)
        {
            return new SingleWindowReceiptParseResult
            {
                BusinessType = SingleWindowBusinessType.CustomsCoo,
                ReceiptKind = SingleWindowReceiptKind.CustomsCooBusinessReceipt,
                BusinessStatus = status,
                OccurredAt = occurredAt
            };
        }
    }
}
