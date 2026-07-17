namespace ExportDocManager.Utils
{
    public static class UserFacingTextHelper
    {
        public static string BuildEntityListEmptyState(
            string entityDisplayName,
            string searchKeyword,
            string defaultActionHint,
            string searchActionHint = "")
        {
            var normalizedName = string.IsNullOrWhiteSpace(entityDisplayName) ? "数据" : entityDisplayName.Trim();
            var normalizedKeyword = string.IsNullOrWhiteSpace(searchKeyword) ? string.Empty : searchKeyword.Trim();

            if (!string.IsNullOrWhiteSpace(normalizedKeyword))
            {
                return $"当前没有找到匹配“{normalizedKeyword}”的{normalizedName}。"
                    + Environment.NewLine + Environment.NewLine
                    + (string.IsNullOrWhiteSpace(searchActionHint)
                        ? "可以试试换个关键词，或清空搜索后查看全部列表。"
                        : searchActionHint.Trim());
            }

            return $"当前还没有{normalizedName}。"
                + Environment.NewLine + Environment.NewLine
                + (string.IsNullOrWhiteSpace(defaultActionHint)
                    ? "可以先新增一条记录。"
                    : defaultActionHint.Trim());
        }

        public static string BuildActionSuccessMessage(
            string actionName,
            string targetDisplayName,
            string detail = "",
            string nextStepHint = "")
        {
            var actionText = string.IsNullOrWhiteSpace(actionName) ? "操作完成" : actionName.Trim();
            var targetText = string.IsNullOrWhiteSpace(targetDisplayName) ? string.Empty : targetDisplayName.Trim();
            var detailText = string.IsNullOrWhiteSpace(detail) ? string.Empty : detail.Trim();
            var nextStepText = string.IsNullOrWhiteSpace(nextStepHint) ? string.Empty : nextStepHint.Trim();

            var parts = new List<string> { string.IsNullOrWhiteSpace(targetText) ? actionText : $"{actionText}：{targetText}" };
            if (!string.IsNullOrWhiteSpace(detailText))
            {
                parts.Add(detailText);
            }

            if (!string.IsNullOrWhiteSpace(nextStepText))
            {
                parts.Add($"下一步建议：{nextStepText}");
            }

            return string.Join(Environment.NewLine, parts);
        }

        public static string BuildActionFailureMessage(
            string actionName,
            string targetDisplayName = "",
            string suggestion = "")
        {
            var actionText = string.IsNullOrWhiteSpace(actionName) ? "操作失败" : $"{actionName.Trim()}失败";
            var targetText = string.IsNullOrWhiteSpace(targetDisplayName) ? string.Empty : $"对象：{targetDisplayName.Trim()}";
            var suggestionText = string.IsNullOrWhiteSpace(suggestion) ? "请检查输入内容或稍后重试。" : suggestion.Trim();

            return string.IsNullOrWhiteSpace(targetText)
                ? $"{actionText}{Environment.NewLine}{Environment.NewLine}{suggestionText}"
                : $"{actionText}{Environment.NewLine}{targetText}{Environment.NewLine}{Environment.NewLine}{suggestionText}";
        }

        public static string BuildEntitySaveSuccessMessage(
            string entityDisplayName,
            string targetDisplayName = "",
            bool isNew = false,
            string detail = "",
            string nextStepHint = "")
        {
            var normalizedEntityName = string.IsNullOrWhiteSpace(entityDisplayName) ? "记录" : entityDisplayName.Trim();
            return BuildActionSuccessMessage(
                isNew ? $"{normalizedEntityName}已新增" : $"{normalizedEntityName}已保存",
                targetDisplayName,
                detail,
                nextStepHint);
        }

        public static string BuildEntityDeleteSuccessMessage(
            string entityDisplayName,
            string targetDisplayName = "",
            string detail = "",
            string nextStepHint = "")
        {
            var normalizedEntityName = string.IsNullOrWhiteSpace(entityDisplayName) ? "记录" : entityDisplayName.Trim();
            return BuildActionSuccessMessage(
                $"{normalizedEntityName}已删除",
                targetDisplayName,
                detail,
                nextStepHint);
        }

        public static string BuildImportSuccessMessage(
            string targetDisplayName,
            string detail = "",
            string nextStepHint = "")
        {
            return BuildActionSuccessMessage("导入成功", targetDisplayName, detail, nextStepHint);
        }

        public static string BuildExportSuccessMessage(
            string targetDisplayName,
            string detail = "",
            string nextStepHint = "")
        {
            return BuildActionSuccessMessage("导出成功", targetDisplayName, detail, nextStepHint);
        }
    }
}
