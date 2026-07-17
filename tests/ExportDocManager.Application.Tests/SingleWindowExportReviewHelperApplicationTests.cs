using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowExportReviewHelperApplicationTests
    {
        [Fact]
        public void BuildGroups_ShouldInferConcreteNavigationTargetsFromIssueMessages()
        {
            var customsGroups = SingleWindowExportReviewHelper.BuildGroups(
                SingleWindowBusinessType.CustomsCoo,
                [
                    (SingleWindowExportIssueSeverity.Error, "商品HS编码(GNo=2)不能为空。")
                ]);
            var customsIssue = Assert.Single(Assert.Single(customsGroups).Issues);

            Assert.Equal("明细项目", customsIssue.GroupKey);
            Assert.Equal("HSCode", customsIssue.NavigationTarget.PropertyKey);
            Assert.Equal(2, customsIssue.NavigationTarget.GoodsLineNo);

            var customsRatioGroups = SingleWindowExportReviewHelper.BuildGroups(
                SingleWindowBusinessType.CustomsCoo,
                [
                    (SingleWindowExportIssueSeverity.Error, "RCEP 进口成份比例(GNo=1)不能为空。")
                ]);
            var customsRatioIssue = Assert.Single(Assert.Single(customsRatioGroups).Issues);

            Assert.Equal("明细项目", customsRatioIssue.GroupKey);
            Assert.Equal("ICompPrpr", customsRatioIssue.NavigationTarget.PropertyKey);
            Assert.Equal(1, customsRatioIssue.NavigationTarget.GoodsLineNo);

            var acdGroups = SingleWindowExportReviewHelper.BuildGroups(
                SingleWindowBusinessType.AgentConsignment,
                [
                    (SingleWindowExportIssueSeverity.Warning, "申报单位(被委托方)海关10位编码缺失或格式不正确。")
                ]);
            var acdIssue = Assert.Single(Assert.Single(acdGroups).Issues);

            Assert.Equal("申报要素", acdIssue.GroupKey);
            Assert.Equal("AgentCode", acdIssue.NavigationTarget.PropertyKey);
        }
    }
}
