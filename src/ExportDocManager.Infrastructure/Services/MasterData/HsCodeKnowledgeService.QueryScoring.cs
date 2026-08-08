using System.Security.Cryptography;
using System.Text;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Utils;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Services.MasterData
{
    public sealed partial class HsCodeKnowledgeService
    {
        private static int ScoreText(string query, HashSet<string> queryGrams, string candidate)
        {
            if (string.IsNullOrWhiteSpace(query) || string.IsNullOrWhiteSpace(candidate)) return 0;
            if (string.Equals(query, candidate, StringComparison.Ordinal)) return 90;
            int score = candidate.Contains(query, StringComparison.Ordinal) ? 72 : query.Contains(candidate, StringComparison.Ordinal) ? 62 : 0;
            var candidateGrams = HsCodeSearchTextNormalizer.BuildNgrams(candidate);
            int intersection = CountIntersection(queryGrams, candidateGrams);
            int denominator = queryGrams.Count + candidateGrams.Count;
            int dice = denominator == 0 ? 0 : (int)Math.Round(intersection * 120d / denominator);
            int relatedBoost = HsCodeSearchTextNormalizer.HasRelatedMatch(query, candidate) ? 12 : 0;
            return Math.Clamp(Math.Max(score, dice) + relatedBoost, 0, 90);
        }

        private static AttributeAssessment AssessAttributes(string query, string candidate)
        {
            var reasons = new List<string>();
            var warnings = new List<string>();
            int penalty = 0;

            CompareExclusiveAttribute(query, candidate, "性别", 38, reasons, warnings,
                ["男式", "男童", "男士"], ["女式", "女童", "女士"]);
            CompareExclusiveAttribute(query, candidate, "织造方式", 24, reasons, warnings,
                ["针织", "钩编"], ["梭织", "机织"]);
            CompareCompatibleSetAttribute(query, candidate, "材质", 28, reasons, warnings,
                ["涤纶", "聚酯", "化纤", "粘胶", "氨纶", "锦纶"], ["棉", "全棉", "纯棉"],
                ["丝", "真丝"], ["毛", "羊毛"], ["麻"]);
            CompareExclusiveAttribute(query, candidate, "品类", 32, reasons, warnings,
                ["T恤衫", "T恤"], ["睡衣", "睡衣裤", "睡裙"], ["衬衫"], ["连衣裙"], ["夹克"], ["长裤", "短裤", "西裤", "裤子"]);

            if (reasons.Count == 0 && warnings.Count == 0) reasons.Add("未检测到明显属性冲突");
            return new AttributeAssessment(Math.Min(penalty, 90), reasons, warnings);

            void CompareExclusiveAttribute(
                string left,
                string right,
                string label,
                int conflictPenalty,
                List<string> matched,
                List<string> conflicts,
                params string[][] groups)
            {
                int leftGroup = FindGroup(left, groups);
                int rightGroup = FindGroup(right, groups);
                if (leftGroup < 0) return;
                if (rightGroup < 0)
                {
                    matched.Add($"{label}：查询为{groups[leftGroup][0]}，候选未明确限定");
                    return;
                }
                if (leftGroup == rightGroup)
                    matched.Add($"{label}：{groups[leftGroup][0]}一致");
                else
                {
                    penalty += conflictPenalty;
                    conflicts.Add($"{label}冲突：查询为{groups[leftGroup][0]}，候选为{groups[rightGroup][0]}");
                }
            }

            void CompareCompatibleSetAttribute(
                string left,
                string right,
                string label,
                int conflictPenalty,
                List<string> matched,
                List<string> conflicts,
                params string[][] groups)
            {
                var leftGroups = FindGroups(NormalizeMaterial(left), groups);
                var rightGroups = FindGroups(NormalizeMaterial(right), groups);
                if (leftGroups.Count == 0) return;
                if (rightGroups.Count == 0)
                {
                    matched.Add($"{label}：查询为{groups[leftGroups[0]][0]}，候选未明确限定");
                    return;
                }
                int common = leftGroups.Intersect(rightGroups).FirstOrDefault(-1);
                if (common >= 0)
                    matched.Add($"{label}：包含{groups[common][0]}");
                else
                {
                    penalty += conflictPenalty;
                    conflicts.Add($"{label}冲突：查询为{groups[leftGroups[0]][0]}，候选为{groups[rightGroups[0]][0]}");
                }
            }

            static int FindGroup(string value, string[][] groups)
            {
                for (int index = 0; index < groups.Length; index++)
                    if (groups[index].Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase))) return index;
                return -1;
            }

            static List<int> FindGroups(string value, string[][] groups) => groups
                .Select((tokens, index) => new { tokens, index })
                .Where(item => item.tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase)))
                .Select(item => item.index)
                .ToList();

            static string NormalizeMaterial(string value) =>
                (value ?? string.Empty).Replace("人棉", "粘胶", StringComparison.OrdinalIgnoreCase);
        }

        internal static string NormalizeSearchText(string value)
            => HsCodeSearchTextNormalizer.Normalize(value);

        private static int CountIntersection(HashSet<string> left, HashSet<string> right)
        {
            HashSet<string> smaller = left.Count <= right.Count ? left : right;
            HashSet<string> larger = ReferenceEquals(smaller, left) ? right : left;
            int count = 0;
            foreach (string value in smaller)
            {
                if (larger.Contains(value))
                {
                    count++;
                }
            }
            return count;
        }

        internal static string BuildFingerprint(params string[] values) =>
            Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", values.Select(value => (value ?? string.Empty).Trim().ToUpperInvariant())))));

    }
}
