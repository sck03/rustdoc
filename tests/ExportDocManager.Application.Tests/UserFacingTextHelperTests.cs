using ExportDocManager.Utils;

namespace ExportDocManager.Application.Tests
{
    public class UserFacingTextHelperTests
    {
        [Fact]
        public void BuildEntityListEmptyState_ShouldUseDefaultHint_WhenListIsEmpty()
        {
            string message = UserFacingTextHelper.BuildEntityListEmptyState(
                "客户",
                "",
                "可以先新增一个客户。");

            Assert.Equal(
                $"当前还没有客户。{Environment.NewLine}{Environment.NewLine}可以先新增一个客户。",
                message);
        }

        [Fact]
        public void BuildEntityListEmptyState_ShouldUseSearchHint_WhenKeywordIsPresent()
        {
            string message = UserFacingTextHelper.BuildEntityListEmptyState(
                "商品",
                "  widget  ",
                "默认提示",
                "请换个关键词。");

            Assert.Equal(
                $"当前没有找到匹配“widget”的商品。{Environment.NewLine}{Environment.NewLine}请换个关键词。",
                message);
        }

        [Fact]
        public void BuildActionSuccessMessage_ShouldIncludeTargetDetailAndNextStep()
        {
            string message = UserFacingTextHelper.BuildActionSuccessMessage(
                "保存完成",
                "INV-001",
                "已同步明细。",
                "继续导出单证。");

            Assert.Equal(
                $"保存完成：INV-001{Environment.NewLine}已同步明细。{Environment.NewLine}下一步建议：继续导出单证。",
                message);
        }

        [Fact]
        public void BuildActionFailureMessage_ShouldUseDefaultSuggestion_WhenSuggestionIsMissing()
        {
            string message = UserFacingTextHelper.BuildActionFailureMessage("保存发票", "INV-001");

            Assert.Equal(
                $"保存发票失败{Environment.NewLine}对象：INV-001{Environment.NewLine}{Environment.NewLine}请检查输入内容或稍后重试。",
                message);
        }

        [Fact]
        public void BuildEntitySuccessMessages_ShouldKeepLegacyActionPrefixes()
        {
            Assert.Equal("客户已新增：ACME", UserFacingTextHelper.BuildEntitySaveSuccessMessage("客户", "ACME", isNew: true));
            Assert.Equal("客户已保存：ACME", UserFacingTextHelper.BuildEntitySaveSuccessMessage("客户", "ACME"));
            Assert.Equal("客户已删除：ACME", UserFacingTextHelper.BuildEntityDeleteSuccessMessage("客户", "ACME"));
        }

        [Fact]
        public void BuildImportAndExportMessages_ShouldReuseActionSuccessFormat()
        {
            Assert.Equal("导入成功：单据包", UserFacingTextHelper.BuildImportSuccessMessage("单据包"));
            Assert.Equal("导出成功：报表", UserFacingTextHelper.BuildExportSuccessMessage("报表"));
        }
    }
}
