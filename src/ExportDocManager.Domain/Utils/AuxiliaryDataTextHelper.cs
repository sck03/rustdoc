using ExportDocManager.Models.Entities;

namespace ExportDocManager.Utils
{
    public static class AuxiliaryDataTextHelper
    {
        public static void NormalizePort(Port port)
        {
            if (port == null)
            {
                return;
            }

            port.NameEN = TextSearchHelper.NormalizeValue(port.NameEN);
            port.NameCN = TextSearchHelper.NormalizeValue(port.NameCN);
            port.Code = TextSearchHelper.NormalizeUpperValue(port.Code);
            port.Country = TextSearchHelper.NormalizeValue(port.Country);
        }

        public static void NormalizeUnit(Unit unit)
        {
            if (unit == null)
            {
                return;
            }

            unit.NameEN = TextSearchHelper.NormalizeValue(unit.NameEN);
            unit.NameCN = TextSearchHelper.NormalizeValue(unit.NameCN);
            unit.Code = TextSearchHelper.NormalizeUpperValue(unit.Code);
        }

        public static string BuildPortDisplayName(Port port)
        {
            return BuildDisplayName(port?.Code, port?.NameEN, port?.NameCN);
        }

        public static string BuildUnitDisplayName(Unit unit)
        {
            return BuildDisplayName(unit?.Code, unit?.NameEN, unit?.NameCN);
        }

        public static string BuildDisplayName(string code, string nameEn, string nameCn)
        {
            var normalizedCode = TextSearchHelper.NormalizeValue(code);
            var normalizedNameEn = TextSearchHelper.NormalizeValue(nameEn);
            var normalizedNameCn = TextSearchHelper.NormalizeValue(nameCn);

            if (!string.IsNullOrWhiteSpace(normalizedCode) && !string.IsNullOrWhiteSpace(normalizedNameEn))
            {
                return $"{normalizedCode} / {normalizedNameEn}";
            }

            if (!string.IsNullOrWhiteSpace(normalizedCode))
            {
                return normalizedCode;
            }

            return !string.IsNullOrWhiteSpace(normalizedNameEn) ? normalizedNameEn : normalizedNameCn;
        }
    }
}
