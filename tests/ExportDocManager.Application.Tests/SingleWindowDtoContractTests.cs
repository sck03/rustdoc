using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowDtoContractTests
    {
        [Fact]
        public void DisplayTextHelper_ShouldKeepBusinessTypeNames()
        {
            Assert.Equal("海关原产地证", SingleWindowDisplayTextHelper.GetBusinessTypeDisplayName(SingleWindowBusinessType.CustomsCoo));
            Assert.Equal("报关代理委托", SingleWindowDisplayTextHelper.GetBusinessTypeDisplayName("AgentConsignment"));
        }

        [Fact]
        public void DisplayTextHelper_ShouldKeepReceiptStatusNames()
        {
            Assert.Equal("已受理", SingleWindowDisplayTextHelper.GetBusinessStatusDisplayName(SingleWindowReceiptBusinessStatus.Accepted));
            Assert.Equal("已退回", SingleWindowDisplayTextHelper.GetBusinessStatusDisplayName("Rejected"));
        }

        [Fact]
        public void OperationCenterPageResult_ShouldCalculateTotalPages()
        {
            var result = new SingleWindowOperationCenterPageResult
            {
                TotalCount = 101,
                PageSize = 50
            };

            Assert.Equal(3, result.TotalPages);
        }

        [Fact]
        public void ExportReview_ShouldAggregateIssueCounts()
        {
            var review = new SingleWindowExportReview
            {
                Groups =
                [
                    new SingleWindowExportIssueGroup
                    {
                        Issues =
                        [
                            new SingleWindowExportIssue { Severity = SingleWindowExportIssueSeverity.Error },
                            new SingleWindowExportIssue { Severity = SingleWindowExportIssueSeverity.Warning },
                        ]
                    },
                    new SingleWindowExportIssueGroup
                    {
                        Issues =
                        [
                            new SingleWindowExportIssue { Severity = SingleWindowExportIssueSeverity.Error },
                        ]
                    }
                ]
            };

            Assert.True(review.HasIssues);
            Assert.Equal(2, review.TotalErrorCount);
            Assert.Equal(1, review.TotalWarningCount);
        }
    }
}
