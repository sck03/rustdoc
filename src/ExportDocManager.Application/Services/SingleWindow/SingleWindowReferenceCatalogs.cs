using System.Threading;
using ExportDocManager.Models.DTOs.SingleWindow;
using ExportDocManager.ViewModels;

namespace ExportDocManager.Services.SingleWindow
{
    public static class SingleWindowReferenceCatalogs
    {
        private static Func<SingleWindowReferenceCatalogModel> _referenceCatalogSnapshotLoader;
        private static Lazy<IReadOnlyList<SelectionOption<string>>> _acdTradeModeOptionsHolder = new(BuildAcdTradeModeOptions);
        private static Lazy<IReadOnlyList<SelectionOption<string>>> _acdCountryOptionsHolder = new(BuildAcdCountryOptions);

        public static void ConfigureReferenceCatalogSnapshotLoader(Func<SingleWindowReferenceCatalogModel> loader)
        {
            ArgumentNullException.ThrowIfNull(loader);
            Volatile.Write(ref _referenceCatalogSnapshotLoader, loader);
            Reload();
        }

        public static IReadOnlyList<SelectionOption<string>> GetAcdTradeModeOptions() => _acdTradeModeOptionsHolder.Value;

        public static IReadOnlyList<SelectionOption<string>> GetAcdCountryOptions() => _acdCountryOptionsHolder.Value;

        public static void Reload()
        {
            ResetLazy(ref _acdTradeModeOptionsHolder, BuildAcdTradeModeOptions);
            ResetLazy(ref _acdCountryOptionsHolder, BuildAcdCountryOptions);
        }

        private static IReadOnlyList<SelectionOption<string>> BuildAcdTradeModeOptions()
        {
            var catalog = LoadReferenceCatalogSnapshot();
            var source = catalog.AcdTradeModes?.Count > 0
                ? catalog.AcdTradeModes
                : BuildFallbackCatalog().AcdTradeModes;

            return source
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code) && !string.IsNullOrWhiteSpace(item.Name))
                .OrderBy(item => item.Code, StringComparer.Ordinal)
                .Select(item => new SelectionOption<string>(
                    item.Code.Trim(),
                    string.IsNullOrWhiteSpace(item.Description)
                        ? $"{item.Code.Trim()}：{item.Name.Trim()}"
                        : $"{item.Code.Trim()}：{item.Name.Trim()} - {item.Description.Trim()}"))
                .Prepend(new SelectionOption<string>(string.Empty, string.Empty))
                .ToList();
        }

        private static IReadOnlyList<SelectionOption<string>> BuildAcdCountryOptions()
        {
            var catalog = LoadReferenceCatalogSnapshot();
            var source = catalog.AcdCountries?.Count > 0
                ? catalog.AcdCountries
                : BuildFallbackCatalog().AcdCountries;

            return source
                .Where(item => item != null && !string.IsNullOrWhiteSpace(item.Code) && !string.IsNullOrWhiteSpace(item.ChineseName))
                .OrderBy(item => item.Code, StringComparer.Ordinal)
                .Select(item => new SelectionOption<string>(
                    item.Code.Trim(),
                    $"{item.Code.Trim()}：{item.ChineseName.Trim()}" +
                    (string.IsNullOrWhiteSpace(item.EnglishName) ? string.Empty : $" / {item.EnglishName.Trim()}")))
                .Prepend(new SelectionOption<string>(string.Empty, string.Empty))
                .ToList();
        }

        private static SingleWindowReferenceCatalogModel LoadReferenceCatalogSnapshot()
        {
            try
            {
                var loader = Volatile.Read(ref _referenceCatalogSnapshotLoader);
                return loader?.Invoke() ?? BuildFallbackCatalog();
            }
            catch
            {
                return BuildFallbackCatalog();
            }
        }

        private static SingleWindowReferenceCatalogModel BuildFallbackCatalog()
        {
            return new SingleWindowReferenceCatalogModel
            {
                AcdCountries =
                [
                    new() { Code = "110", ChineseName = "香港", EnglishName = "Hong Kong", Aliases = ["香港", "HONGKONG", "HK"] },
                    new() { Code = "112", ChineseName = "印度尼西亚", EnglishName = "Indonesia", Aliases = ["印尼", "印度尼西亚", "INDONESIA"] },
                    new() { Code = "116", ChineseName = "日本", EnglishName = "Japan", Aliases = ["日本", "JAPAN"] },
                    new() { Code = "121", ChineseName = "澳门", EnglishName = "Macao", Aliases = ["澳门", "MACAO", "MACAU"] },
                    new() { Code = "122", ChineseName = "马来西亚", EnglishName = "Malaysia", Aliases = ["马来西亚", "MALAYSIA"] },
                    new() { Code = "130", ChineseName = "新加坡", EnglishName = "Singapore", Aliases = ["新加坡", "SINGAPORE"] },
                    new() { Code = "131", ChineseName = "韩国", EnglishName = "Korea,Rep.", Aliases = ["韩国", "KOREA", "SOUTHKOREA"] },
                    new() { Code = "138", ChineseName = "越南", EnglishName = "Viet Nam", Aliases = ["越南", "VIETNAM"] },
                    new() { Code = "142", ChineseName = "中国", EnglishName = "China", Aliases = ["中国", "CHINA", "CN", "CHN"] },
                    new() { Code = "143", ChineseName = "台澎金马关税区", EnglishName = "Taiwan, Prov.of China", Aliases = ["台湾", "中国台湾", "TAIWAN"] },
                    new() { Code = "301", ChineseName = "比利时", EnglishName = "Belgium", Aliases = ["比利时", "BELGIUM"] },
                    new() { Code = "303", ChineseName = "英国", EnglishName = "United Kingdom", Aliases = ["英国", "UNITEDKINGDOM", "UK"] },
                    new() { Code = "304", ChineseName = "德国", EnglishName = "Germany", Aliases = ["德国", "GERMANY"] },
                    new() { Code = "305", ChineseName = "法国", EnglishName = "France", Aliases = ["法国", "FRANCE"] },
                    new() { Code = "309", ChineseName = "荷兰", EnglishName = "Netherlands", Aliases = ["荷兰", "NETHERLANDS"] },
                    new() { Code = "312", ChineseName = "西班牙", EnglishName = "Spain", Aliases = ["西班牙", "SPAIN"] },
                    new() { Code = "326", ChineseName = "挪威", EnglishName = "Norway", Aliases = ["挪威", "NORWAY"] },
                    new() { Code = "331", ChineseName = "瑞士", EnglishName = "Switzerland", Aliases = ["瑞士", "SWITZERLAND"] },
                    new() { Code = "337", ChineseName = "俄罗斯联邦", EnglishName = "Russian Federation", Aliases = ["俄罗斯", "RUSSIA", "RUSSIANFEDERATION"] },
                    new() { Code = "402", ChineseName = "阿根廷", EnglishName = "Argentina", Aliases = ["阿根廷", "ARGENTINA"] },
                    new() { Code = "409", ChineseName = "巴西", EnglishName = "Brazil", Aliases = ["巴西", "BRAZIL"] },
                    new() { Code = "411", ChineseName = "智利", EnglishName = "Chile", Aliases = ["智利", "CHILE"] },
                    new() { Code = "414", ChineseName = "哥斯达黎加", EnglishName = "Costa Rica", Aliases = ["哥斯达黎加", "COSTARICA"] },
                    new() { Code = "417", ChineseName = "厄瓜多尔", EnglishName = "Ecuador", Aliases = ["厄瓜多尔", "ECUADOR"] },
                    new() { Code = "427", ChineseName = "墨西哥", EnglishName = "Mexico", Aliases = ["墨西哥", "MEXICO"] },
                    new() { Code = "431", ChineseName = "巴拉圭", EnglishName = "Paraguay", Aliases = ["巴拉圭", "PARAGUAY"] },
                    new() { Code = "432", ChineseName = "秘鲁", EnglishName = "Peru", Aliases = ["秘鲁", "PERU"] },
                    new() { Code = "501", ChineseName = "加拿大", EnglishName = "Canada", Aliases = ["加拿大", "CANADA"] },
                    new() { Code = "502", ChineseName = "美国", EnglishName = "United States", Aliases = ["美国", "UNITEDSTATES", "USA", "US"] },
                    new() { Code = "503", ChineseName = "澳大利亚", EnglishName = "Australia", Aliases = ["澳大利亚", "AUSTRALIA"] },
                    new() { Code = "504", ChineseName = "新西兰", EnglishName = "New Zealand", Aliases = ["新西兰", "NEWZEALAND"] },
                    new() { Code = "699", ChineseName = "大洋洲其他国家(地区)", EnglishName = "Oth. Ocean. nes", Aliases = ["大洋洲其他国家", "大洋洲其他地区"] },
                    new() { Code = "701", ChineseName = "国(地)别不详", EnglishName = "Countries(reg.) unknown", Aliases = ["不详"] },
                    new() { Code = "702", ChineseName = "联合国及机构和国际组织", EnglishName = "UN and oth. int'l  org.", Aliases = ["联合国及机构和国际组织"] },
                    new() { Code = "999", ChineseName = "中性包装原产国别", EnglishName = "Conutries of Neutral Package", Aliases = ["中性包装原产国别"] }
                ],
                AcdTradeModes =
                [
                    new() { Code = "0110", Name = "一般贸易", Description = "一般贸易", Aliases = ["一般贸易"] },
                    new() { Code = "0130", Name = "易货贸易", Description = "易货贸易", Aliases = ["易货贸易"] },
                    new() { Code = "0139", Name = "旅游购物商品", Description = "用于旅游者五万美元以下的出口小批量订货", Aliases = ["旅游购物商品"] },
                    new() { Code = "0214", Name = "来料加工", Description = "来料加工装配贸易进口料件及加工出口货物", Aliases = ["来料加工"] },
                    new() { Code = "0615", Name = "进料对口", Description = "进料加工对口合同项下的进口料件及加工出口货物", Aliases = ["进料对口", "进料加工"] },
                    new() { Code = "1215", Name = "保税工厂", Description = "保税工厂", Aliases = ["保税工厂"] },
                    new() { Code = "4019", Name = "海关特殊监管区域物流货物", Description = "海关特殊监管区域物流货物", Aliases = ["海关特殊监管区域物流货物"] },
                    new() { Code = "6033", Name = "物流中心进出境货", Description = "保税物流中心与境外之间进出仓储货物", Aliases = ["物流中心进出境货"] },
                    new() { Code = "9600", Name = "内贸货物跨境运输", Description = "内贸货物跨境运输", Aliases = ["内贸货物跨境运输"] },
                    new() { Code = "9739", Name = "其他贸易", Description = "其他贸易", Aliases = ["其他贸易"] },
                    new() { Code = "9800", Name = "租赁征税", Description = "租赁期一年及以上的租赁贸易货物的租金", Aliases = ["租赁征税"] },
                    new() { Code = "9900", Name = "其他", Description = "其他", Aliases = ["其他"] }
                ]
            };
        }

        private static void ResetLazy(
            ref Lazy<IReadOnlyList<SelectionOption<string>>> holder,
            Func<IReadOnlyList<SelectionOption<string>>> factory)
        {
            holder = new Lazy<IReadOnlyList<SelectionOption<string>>>(factory);
        }
    }
}
