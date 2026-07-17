using System.Collections.Generic;
using System.Text;
using ExportDocManager.ViewModels;

namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooOriginCriteriaCatalog
    {
        private static readonly SelectionOption<string>[] EmptyOptions =
        [
            new(string.Empty, string.Empty)
        ];

        private static readonly SelectionOption<string>[] AustraliaAgreementOptions =
        [
            new(string.Empty, string.Empty),
            new("WO", "WO：完全获得"),
            new("WP", "WP：完全使用原产材料生产"),
            new("PSR", "PSR：产品特定原产地标准")
        ];

        private static readonly SelectionOption<string>[] ChileAgreementOptions =
        [
            new(string.Empty, string.Empty),
            new("WO", "WO：完全获得"),
            new("WP", "WP：完全使用原产材料生产"),
            new("PSR", "PSR：产品特定原产地标准")
        ];

        private static readonly SelectionOption<string>[] KoreaAgreementOptions =
        [
            new(string.Empty, string.Empty),
            new("WO", "WO：完全获得或生产"),
            new("WP", "WP：完全使用双方原产材料生产"),
            new("PSR", "PSR：产品特定原产地标准"),
            new("OP", "OP：特定货物处理规定"),
        ];

        private static readonly SelectionOption<string>[] TaiwanAgreementOptions =
        [
            new(string.Empty, string.Empty),
            new("WO", "WO：完全获得"),
            new("WP", "WP：完全使用双方原产材料生产"),
            new("PSR", "PSR：产品特定原产地标准"),
            new("PSR ACU", "PSR ACU：产品特定原产地标准 + 累积规则"),
            new("PSR DMI", "PSR DMI：产品特定原产地标准 + 微小含量"),
            new("PSR FG", "PSR FG：产品特定原产地标准 + 可互换材料"),
        ];

        private static readonly SelectionOption<string>[] FormEOptions =
        [
            new(string.Empty, string.Empty),
            new("WO", "WO：完全获得或生产"),
            new("PE", "PE：完全使用东盟/中国原产材料生产"),
            new("PSR", "PSR：产品特定原产地规则"),
            new("CTH", "CTH：税号前4位改变")
        ];

        private static readonly SelectionOption<string>[] GspReferenceOptions =
        [
            new(string.Empty, string.Empty),
            new("F", "F：进口成分价值不超过规定比例"),
            new("P", "P：完全原产"),
            new("PK", "PK：完全原产并含巴基斯坦配额/特殊要求"),
            new("W", "W：按 HS 前4位税目标准判定"),
            new("Y", "Y：填写进口成分占 FOB 百分比"),
        ];

        private static readonly SelectionOption<string>[] GeorgiaAgreementOptions =
        [
            new(string.Empty, string.Empty),
            new("WO", "WO：完全获得或生产"),
            new("WP", "WP：完全使用原产材料生产"),
            new("40%", "40%：区域价值成分不低于40%"),
            new("PSR", "PSR：产品特定原产地规则")
        ];

        private static readonly SelectionOption<string>[] RcepOptions =
        [
            new(string.Empty, string.Empty),
            new("CR", "CR：完全获得或生产"),
            new("CTC", "CTC：税则归类改变"),
            new("PE", "PE：完全使用原产材料生产"),
            new("RVC", "RVC：区域价值成分"),
            new("WO", "WO：完全获得或生产")
        ];

        private static readonly SelectionOption<string>[] FormESubOptions =
        [
            new(string.Empty, string.Empty),
            new("1", "1：适用区域价值成分（RVC）标准"),
            new("2", "2：适用归类改变标准（CTC）"),
            new("3", "3：适用加工工序标准")
        ];

        private static readonly HashSet<string> GspCertificateTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "G"
        };

        public static IReadOnlyList<SelectionOption<string>> GetOriginCriteriaOptions(string certType)
        {
            string normalized = Normalize(certType);
            if (string.Equals(normalized, "C", StringComparison.Ordinal))
            {
                return EmptyOptions;
            }

            if (string.Equals(normalized, "A", StringComparison.Ordinal))
            {
                return AustraliaAgreementOptions;
            }

            if (string.Equals(normalized, "F", StringComparison.Ordinal))
            {
                return ChileAgreementOptions;
            }

            if (string.Equals(normalized, "K", StringComparison.Ordinal))
            {
                return KoreaAgreementOptions;
            }

            if (string.Equals(normalized, "H", StringComparison.Ordinal))
            {
                return TaiwanAgreementOptions;
            }

            return normalized switch
            {
                "E" => FormEOptions,
                "G" => GspReferenceOptions,
                "GE" => GeorgiaAgreementOptions,
                "RC" => RcepOptions,
                _ => EmptyOptions
            };
        }

        public static IReadOnlyList<SelectionOption<string>> GetOriginCriteriaSubOptions(string certType, string originCriteria)
        {
            string normalizedCertType = Normalize(certType);
            string normalizedOriginCriteria = Normalize(originCriteria);

            return normalizedCertType switch
            {
                "E" when string.Equals(normalizedOriginCriteria, "PSR", StringComparison.Ordinal) => FormESubOptions,
                _ => EmptyOptions
            };
        }

        public static string GetOriginCriteriaSubHelpText(string certType, string originCriteria)
        {
            string normalizedCertType = Normalize(certType);
            string normalizedOriginCriteria = Normalize(originCriteria);

            return normalizedCertType switch
            {
                "E" when string.Equals(normalizedOriginCriteria, "PSR", StringComparison.Ordinal)
                    => "FORM E：主标准为 PSR 时，再选子标准。1=RVC，2=CTC，3=PROCESS RULE。",
                "E" => "FORM E：子标准通常只在 PSR 场景填写；WO、PE 等一般留空。",
                "RC" => "RCEP：当前不单独使用子标准，通常留空。",
                "GE" => "格鲁吉亚证书：原产标准直接使用 WO / WP / 40% / PSR，子标准通常留空。",
                "G" => "普惠制证书：子标准通常留空。",
                "C" => "一般原产地证：官方网页通常不填写原产标准，子标准留空。",
                "A" => "中澳证书：原产标准直接使用 WO / WP / PSR，子标准通常留空。",
                "F" => "中智证书：原产标准直接使用 WO / WP / PSR，子标准通常留空。",
                "K" => "中韩证书：原产标准直接使用 WO / WP / PSR / OP，子标准通常留空。",
                "H" => "海峡证书：ACU / DMI / FG 作为主标准组合值直接选择，子标准通常留空。",
                _ => "当前证型下，子标准通常不单独填写。"
            };
        }

        public static string GetOriginCriteriaSubCueText(string certType, string originCriteria)
        {
            string normalizedCertType = Normalize(certType);
            string normalizedOriginCriteria = Normalize(originCriteria);

            return normalizedCertType switch
            {
                "E" when string.Equals(normalizedOriginCriteria, "PSR", StringComparison.Ordinal) => "FORM E：1 / 2 / 3",
                "RC" => "RCEP 子标准通常留空",
                "GE" => "格鲁吉亚证书通常留空",
                "G" => "普惠制证书通常留空",
                "C" => "一般原产地证通常留空",
                "A" => "中澳证书通常留空",
                "F" => "中智证书通常留空",
                "K" => "中韩证书通常留空",
                "H" => "海峡证书通常留空",
                _ => "按当前证型判断是否需要"
            };
        }

        public static bool UsesOriginCriteriaRef(string certType)
        {
            string normalized = Normalize(certType);
            return string.Equals(normalized, "E", StringComparison.Ordinal) ||
                   GspCertificateTypes.Contains(normalized);
        }

        public static string GetOriginCriteriaRefHelpText(string certType, string originCriteria)
        {
            string normalizedCertType = Normalize(certType);
            string normalizedOriginCriteria = Normalize(originCriteria);

            return normalizedCertType switch
            {
                "E" when string.Equals(normalizedOriginCriteria, "PSR", StringComparison.Ordinal)
                    => "FORM E：PSR 场景一般优先填写子标准；辅助项通常留空。",
                "E"
                    => "FORM E：辅助项通常录入进口成分比例。可直接输入 40，系统会自动规范成 40%。",
                "G" when string.Equals(normalizedOriginCriteria, "W", StringComparison.Ordinal)
                    => "普惠制证书：W 时辅助项按 HS 编码前 4 位自动回填为 XX.XX，不可修改。",
                "G" when string.Equals(normalizedOriginCriteria, "Y", StringComparison.Ordinal)
                    => "普惠制证书：Y 时填写进口成分占 FOB 的百分比，且不得超过 50%。",
                "G" => "普惠制证书：P/PK/F 一般不填写辅助项。",
                "RC" => "RCEP：进口成分比例请填在“进口成份比例(ICompPrpr)”中，原产标准辅助项通常不显示。",
                "C" => "一般原产地证：官方网页通常不填写原产标准辅助项。",
                "A" => "中澳证书：原产标准辅助项通常不单独填写。",
                "F" => "中智证书：原产标准辅助项通常不单独填写。",
                "GE" => "格鲁吉亚证书：原产标准辅助项通常不单独填写。",
                "K" => "中韩证书：原产标准辅助项通常不单独填写。",
                "H" => "海峡证书：ACU / DMI / FG 作为主标准组合值直接填写，辅助项通常不单独填写。",
                _ => "当前证型下，辅助项通常不单独填写。"
            };
        }

        public static string GetOriginCriteriaRefCueText(string certType, string originCriteria)
        {
            string normalizedCertType = Normalize(certType);
            string normalizedOriginCriteria = Normalize(originCriteria);

            return normalizedCertType switch
            {
                "E" when string.Equals(normalizedOriginCriteria, "PSR", StringComparison.Ordinal) => "FORM E / PSR：通常留空",
                "E" => "FORM E 比例，例如 40%",
                "G" when string.Equals(normalizedOriginCriteria, "W", StringComparison.Ordinal) => "自动带出 HS 前4位",
                "G" when string.Equals(normalizedOriginCriteria, "Y", StringComparison.Ordinal) => "比例，例如 50%",
                "G" => "普惠制证书通常留空",
                "RC" => "RCEP 通常不使用",
                "C" => "一般原产地证通常留空",
                "A" => "中澳证书通常留空",
                "F" => "中智证书通常留空",
                "GE" => "格鲁吉亚证书通常留空",
                _ => "当前证型通常留空"
            };
        }

        public static string GetOriginCriteriaHelpText(string certType)
        {
            string normalized = Normalize(certType);
            return normalized switch
            {
                "RC" => "RCEP：按官方网页下拉预置 CR / CTC / PE / RVC / WO；进口成分比例请填在“进口成份比例”。",
                "E" => "FORM E：官方手册明确给出 WO / PE / PSR / CTH。若选择 PSR，再到下方选择子标准 A / B / C。",
                "G"
                    => "普惠制证书：按网页下拉预置 F / P / PK / W / Y。选择 W 时，辅助项自动按 HS 前 4 位回填；选择 Y 时，辅助项填写比例。",
                "GE"
                    => "格鲁吉亚证书：当前按官方填制说明预置 WO / WP / 40% / PSR，其中 40% 直接填写在主原产标准字段。",
                "C" => "一般原产地证：官方网页通常不填写原产标准，本字段默认留空。",
                "A" => "中澳证书：按官方网页常见下拉预置 WO / WP / PSR。",
                "F" => "中智证书：按官方网页常见下拉预置 WO / WP / PSR。",
                "K" => "中韩证书：官方填制说明明确给出 WO / WP / PSR / OP。",
                "H" => "海峡证书：官方填制说明明确给出 WO / WP / PSR，且在适用累积规则、微小含量或可互换材料时使用 PSR ACU / PSR DMI / PSR FG。",
                _ => "当前证型未内置经官方文本核实的候选项，如确需填写可手工录入。"
            };
        }

        public static string GetOriginCriteriaCueText(string certType)
        {
            string normalized = Normalize(certType);
            return normalized switch
            {
                "RC" => "RCEP：CR / CTC / PE / RVC / WO",
                "E" => "东盟常见：WO / PE / PSR / CTH",
                "G" => "普惠制证书：F / P / PK / W / Y",
                "GE" => "格鲁吉亚：WO / WP / 40% / PSR",
                "C" => "一般原产地证通常留空",
                "A" => "中澳：WO / WP / PSR",
                "F" => "中智：WO / WP / PSR",
                "K" => "中韩：WO / WP / PSR / OP",
                "H" => "海峡：WO / WP / PSR / PSR ACU / PSR DMI / PSR FG",
                _ => "当前证型通常手工输入"
            };
        }

        public static bool UsesOriginCriteriaRefPercent(string certType, string originCriteria)
        {
            string normalizedCertType = Normalize(certType);
            string normalizedOriginCriteria = Normalize(originCriteria);

            return normalizedCertType switch
            {
                "E" => !string.Equals(normalizedOriginCriteria, "PSR", StringComparison.Ordinal),
                "G" => string.Equals(normalizedOriginCriteria, "Y", StringComparison.Ordinal),
                _ => false
            };
        }

        public static int? GetOriginCriteriaRefPercentMax(string certType, string originCriteria)
        {
            string normalizedCertType = Normalize(certType);
            string normalizedOriginCriteria = Normalize(originCriteria);

            return normalizedCertType switch
            {
                "G" when string.Equals(normalizedOriginCriteria, "Y", StringComparison.Ordinal) => 50,
                "E" when !string.Equals(normalizedOriginCriteria, "PSR", StringComparison.Ordinal) => 100,
                _ => null
            };
        }

        public static bool RequiresOriginCriteria(string certType)
        {
            string normalized = Normalize(certType);
            return CustomsCooRuleCatalog.UsesGoodsOriginCriteria(normalized) &&
                   !string.Equals(normalized, "C", StringComparison.Ordinal);
        }

        public static bool RequiresOriginCriteriaRef(string certType, string originCriteria)
        {
            string normalizedCertType = Normalize(certType);
            string normalizedOriginCriteria = Normalize(originCriteria);
            return string.Equals(normalizedCertType, "G", StringComparison.Ordinal) &&
                   (string.Equals(normalizedOriginCriteria, "W", StringComparison.Ordinal) ||
                    string.Equals(normalizedOriginCriteria, "Y", StringComparison.Ordinal));
        }

        public static bool IsValidOriginCriteria(string certType, string originCriteria)
        {
            string normalizedOriginCriteria = Normalize(originCriteria);
            if (string.IsNullOrWhiteSpace(normalizedOriginCriteria))
            {
                return true;
            }

            return GetOriginCriteriaOptions(certType)
                .Any(option => string.Equals(Normalize(option.Value), normalizedOriginCriteria, StringComparison.Ordinal));
        }

        public static bool IsValidOriginCriteriaSub(string certType, string originCriteria, string originCriteriaSub)
        {
            string normalizedSub = NormalizeOriginCriteriaSubInput(certType, originCriteria, originCriteriaSub);
            if (string.IsNullOrWhiteSpace(normalizedSub))
            {
                return true;
            }

            return GetOriginCriteriaSubOptions(certType, originCriteria)
                .Any(option => string.Equals(Normalize(option.Value), normalizedSub, StringComparison.Ordinal));
        }

        public static string NormalizeOriginCriteriaSubInput(string certType, string originCriteria, string value)
        {
            string normalizedCertType = Normalize(certType);
            string normalizedOriginCriteria = Normalize(originCriteria);
            string normalizedValue = Normalize(value);
            if (string.IsNullOrWhiteSpace(normalizedValue))
            {
                return string.Empty;
            }

            if (string.Equals(normalizedCertType, "E", StringComparison.Ordinal) &&
                string.Equals(normalizedOriginCriteria, "PSR", StringComparison.Ordinal))
            {
                return normalizedValue switch
                {
                    "A" => "1",
                    "B" => "2",
                    "C" => "3",
                    _ => normalizedValue
                };
            }

            return normalizedValue;
        }

        public static string NormalizeOriginCriteriaRefInput(string certType, string originCriteria, string value)
            => NormalizeOriginCriteriaRefInput(certType, originCriteria, string.Empty, value);

        public static string NormalizeOriginCriteriaRefInput(string certType, string originCriteria, string hsCode, string value)
        {
            string normalized = value?.Trim() ?? string.Empty;
            if (UsesOriginCriteriaRefHsHeading(certType, originCriteria))
            {
                string hsHeading = FormatHsHeading(hsCode);
                return string.IsNullOrWhiteSpace(hsHeading) ? normalized : hsHeading;
            }

            if (string.IsNullOrWhiteSpace(normalized) || !UsesOriginCriteriaRefPercent(certType, originCriteria))
            {
                return normalized;
            }

            string percentageCore = normalized.EndsWith("%", StringComparison.Ordinal)
                ? normalized[..^1].Trim()
                : normalized;
            if (!decimal.TryParse(percentageCore, out var percentage))
            {
                return normalized;
            }

            string formatted = percentage.ToString("0.##");
            return $"{formatted}%";
        }

        public static bool IsOriginCriteriaRefReadOnly(string certType, string originCriteria) =>
            UsesOriginCriteriaRefHsHeading(certType, originCriteria);

        public static bool UsesOriginCriteriaRefHsHeading(string certType, string originCriteria)
        {
            string normalizedCertType = Normalize(certType);
            string normalizedOriginCriteria = Normalize(originCriteria);
            return GspCertificateTypes.Contains(normalizedCertType) &&
                   string.Equals(normalizedOriginCriteria, "W", StringComparison.Ordinal);
        }

        private static string FormatHsHeading(string hsCode)
        {
            if (string.IsNullOrWhiteSpace(hsCode))
            {
                return string.Empty;
            }

            var digits = new StringBuilder(4);
            foreach (char ch in hsCode)
            {
                if (!char.IsDigit(ch))
                {
                    continue;
                }

                digits.Append(ch);
                if (digits.Length == 4)
                {
                    break;
                }
            }

            if (digits.Length < 4)
            {
                return string.Empty;
            }

            string heading = digits.ToString();
            return $"{heading[..2]}.{heading[2..4]}";
        }

        private static string Normalize(string value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    }
}
