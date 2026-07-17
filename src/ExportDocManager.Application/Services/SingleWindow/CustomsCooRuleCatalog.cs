using System.Collections.Generic;

namespace ExportDocManager.Services.SingleWindow
{
    public static class CustomsCooRuleCatalog
    {
        private static readonly HashSet<string> HeaderProducerCertTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "A",
            "GE",
            "N",
            "L",
            "R",
            "P",
            "F",
            "K",
            "EC",
            "SE",
            "MV"
        };

        private static readonly HashSet<string> HeaderRemarkCertTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "A",
            "GE",
            "N",
            "L",
            "R",
            "P",
            "F",
            "K",
            "SE",
            "MV"
        };

        private static readonly HashSet<string> TaiwanHeaderContactCertTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "H"
        };

        private static readonly HashSet<string> PredictFlagCertTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "H",
            "NI",
            "EC",
            "HD",
            "MV",
            "CG"
        };

        private static readonly HashSet<string> OriginCountryCertTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "TR",
            "PR"
        };

        private static readonly HashSet<string> GoodsOriginCriteriaHiddenCertTypes = new(StringComparer.OrdinalIgnoreCase)
        {
            "C",
            "AD",
            "CA",
            "NI",
            "SE",
            "HD"
        };

        public static bool UsesHeaderProducer(string certType) => HeaderProducerCertTypes.Contains(Normalize(certType));

        public static bool RequiresHeaderProducer(string certType, string thirdPartyInvFlag)
        {
            return string.Equals(Normalize(certType), "EC", StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(thirdPartyInvFlag?.Trim(), "1", StringComparison.Ordinal);
        }

        public static bool UsesHeaderRemark(string certType) => HeaderRemarkCertTypes.Contains(Normalize(certType));

        public static bool UsesExhibitFlag(string certType) =>
            string.Equals(Normalize(certType), "E", StringComparison.OrdinalIgnoreCase);

        public static bool UsesThirdPartyInvoiceFlag(string certType) =>
            string.Equals(Normalize(certType), "H", StringComparison.OrdinalIgnoreCase);

        public static bool UsesTaiwanHeaderContacts(string certType) =>
            TaiwanHeaderContactCertTypes.Contains(Normalize(certType));

        public static bool RequiresTaiwanExporterContacts(string certType, string applyType) =>
            UsesTaiwanHeaderContacts(certType) &&
            string.Equals(applyType?.Trim(), "1", StringComparison.Ordinal);

        public static bool UsesPredictFlag(string certType) =>
            PredictFlagCertTypes.Contains(Normalize(certType));

        public static bool UsesOriginCountryFields(string certType) =>
            OriginCountryCertTypes.Contains(Normalize(certType));

        public static bool UsesProcessingAssembly(string certType) =>
            string.Equals(Normalize(certType), "PR", StringComparison.OrdinalIgnoreCase);

        public static bool UsesGoodsProducerDescription(string certType)
        {
            var normalized = Normalize(certType);
            return string.Equals(normalized, "H", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "RC", StringComparison.OrdinalIgnoreCase);
        }

        public static bool UsesGoodsProducerContactFields(string certType) =>
            string.Equals(Normalize(certType), "H", StringComparison.OrdinalIgnoreCase);

        public static bool UsesGoodsRcepFields(string certType) =>
            string.Equals(Normalize(certType), "RC", StringComparison.OrdinalIgnoreCase);

        public static bool UsesGoodsOriginCriteria(string certType) =>
            !GoodsOriginCriteriaHiddenCertTypes.Contains(Normalize(certType));

        public static bool UsesRcepInvoiceInfo(string certType) =>
            string.Equals(Normalize(certType), "RC", StringComparison.OrdinalIgnoreCase);

        public static bool UsesGoodsOriCriteriaSub(string certType) =>
            string.Equals(Normalize(certType), "E", StringComparison.OrdinalIgnoreCase);

        public static bool RequiresUiTransportDetails(string certType)
        {
            string normalized = Normalize(certType);
            return string.Equals(normalized, "C", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, "G", StringComparison.OrdinalIgnoreCase);
        }

        public static bool RequiresGspTransportAndTrade(string certType) =>
            string.Equals(Normalize(certType), "G", StringComparison.OrdinalIgnoreCase);

        public static bool UsesModificationFields(string certStatus)
        {
            return string.Equals(certStatus?.Trim(), "1", StringComparison.Ordinal) ||
                   string.Equals(certStatus?.Trim(), "2", StringComparison.Ordinal) ||
                   string.Equals(certStatus?.Trim(), "3", StringComparison.Ordinal);
        }

        private static string Normalize(string value) => value?.Trim().ToUpperInvariant() ?? string.Empty;
    }
}
