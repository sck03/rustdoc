using System;
using System.Collections.Generic;
using System.ComponentModel;
using ExportDocManager.Utils;

namespace ExportDocManager.Models
{
    public class AppSettings
    {
        public SystemSettings System { get; set; } = new SystemSettings();
        public BatchExportSettings BatchExport { get; set; } = new BatchExportSettings();
        public List<BatchExportItem> PaymentTemplates { get; set; } = new List<BatchExportItem>();
        public ExcelImportSettings ExcelImport { get; set; } = new ExcelImportSettings();
        public List<ExcelImportSettings> ExcelImportSchemes { get; set; } = new List<ExcelImportSettings>();
        public ExchangeRateSettings ExchangeRate { get; set; } = new ExchangeRateSettings();
        public EmailConfig Email { get; set; } = new EmailConfig();
        public WebDavSettings WebDav { get; set; } = new WebDavSettings();
        public AISettings AI { get; set; } = new AISettings();
        public SingleWindowSettings SingleWindow { get; set; } = new SingleWindowSettings();
    }

    public class SingleWindowSettings
    {
        public CustomsCooDefaultProfile CustomsCooDefaults { get; set; } = new CustomsCooDefaultProfile();
    }

    public class CustomsCooDefaultProfile
    {
        public string ApplName { get; set; } = string.Empty;
        public string Applicant { get; set; } = string.Empty;
        public string ApplTel { get; set; } = string.Empty;
        public string OrgCode { get; set; } = string.Empty;
        public string FetchPlace { get; set; } = string.Empty;
        public string AplAdd { get; set; } = string.Empty;
    }

    public class AISettings
    {
        [DisplayName("AI 服务提供商 API 地址")]
        [Description("支持兼容 OpenAI 接口的大模型 API 地址，例如 https://api.deepseek.com/v1/chat/completions 或 http://localhost:11434/v1/chat/completions (Ollama)")]
        public string ApiEndpoint { get; set; } = "https://api.deepseek.com/v1/chat/completions";

        [DisplayName("AI API Key")]
        [Description("用于调用大模型的 API Key。如果使用本地 Ollama，可以填入任意字符 (如 'ollama')")]
        public string ApiKey { get; set; } = "";

        [DisplayName("AI 模型名称")]
        [Description("例如 deepseek-chat, qwen-max 或本地模型如 qwen2.5:7b")]
        public string ModelName { get; set; } = "deepseek-chat";

        [Browsable(false)] // 隐藏，不在 PropertyGrid 中显示
        public string SystemPrompt { get; set; } = "你是一个专业的国际贸易信用证(L/C)单证审核专家。你的任务是：\n1. 仔细核对信用证条款与实际发票/装箱单数据。\n2. 严格遵循 UCP600 惯例。\n3. 找出所有不符点 (Discrepancies) 并提供修改建议。\n请以清晰、专业的结构输出审查报告。";
    }

    public class BatchExportSettings
    {
        [Browsable(false)]
        public List<BatchExportItem> Items { get; set; } = new List<BatchExportItem>();

        [DisplayName("导出文件命名规则")]
        [Description("支持占位符：{InvoiceNo} {Customer} {DocType} {Date}")]
        public string OutputFileNamePattern { get; set; } = "{InvoiceNo}_{DocType}";

        [DisplayName("导出文件夹命名规则")]
        [Description("支持占位符：{InvoiceNo} {Customer} {Date}")]
        public string OutputFolderPattern { get; set; } = "{InvoiceNo}_Docs_{Date}";

        [DisplayName("导出后自动合并 PDF")]
        public bool MergePdf { get; set; } = true;

        [DisplayName("导出后自动生成 ZIP")]
        public bool ZipAfterExport { get; set; } = true;
    }

    public class BatchExportItem : INotifyPropertyChanged
    {
        private string _name = string.Empty;
        private string _templatePath = string.Empty;
        private bool _isEnabled = true;
        private bool _showSeal = true;
        private string _reportType = string.Empty;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public string TemplatePath
        {
            get => _templatePath;
            set => SetProperty(ref _templatePath, value);
        }

        public bool IsEnabled
        {
            get => _isEnabled;
            set => SetProperty(ref _isEnabled, value);
        }

        public bool ShowSeal
        {
            get => _showSeal;
            set => SetProperty(ref _showSeal, value);
        }

        public string ReportType
        {
            get => _reportType;
            set => SetProperty(ref _reportType, value);
        } // "CommercialInvoice", "PackingList", or "Generic"

        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void SetProperty<T>(ref T field, T value, [System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            OnPropertyChanged(propertyName);
        }
    }

    public class SystemSettings
    {
        [DisplayName("软件名称")]
        [Description("显示在主界面标题栏的软件名称")]
        public string AppName { get; set; } = "外贸业务综合管理系统";

        [DisplayName("默认出口商中文名")]
        [Description("Excel 未提供出口商中文名时用于导入草稿和导出模板；请先填写真实出口商中文公司名，避免把英文发票抬头当作中文名")]
        public string DefaultTemplateExporterNameCn { get; set; } = "";

        [DisplayName("数据备份保留天数")]
        [Description("自动备份文件的保留天数，超过此天数的旧备份将被自动清理 (默认0天，表示关闭自动备份)")]
        public int BackupRetentionDays { get; set; } = 0;

        [DisplayName("PostgreSQL 团队库自动备份")]
        [Description("启用后，团队版/服务端 PostgreSQL 业务数据库会按计划执行 pg_dump 物理备份；SQLite 单机模式不生效")]
        public bool PostgreSqlAutoBackupEnabled { get; set; } = false;

        [DisplayName("PostgreSQL 自动备份周期")]
        [Description("可选 Daily 或 Weekly")]
        public string PostgreSqlAutoBackupSchedule { get; set; } = "Daily";

        [DisplayName("PostgreSQL 自动备份时间")]
        [Description("24 小时制 HH:mm，例如 02:00")]
        public string PostgreSqlAutoBackupTime { get; set; } = "02:00";

        [DisplayName("PostgreSQL 每周备份星期")]
        [Description("0=星期日，1=星期一，依此类推；仅周期为 Weekly 时生效")]
        public int PostgreSqlAutoBackupDayOfWeek { get; set; } = 1;

        [DisplayName("PostgreSQL 备份保留份数")]
        [Description("自动备份完成后保留最近 N 个 .dump 文件，0 表示不按份数清理")]
        public int PostgreSqlAutoBackupRetentionCount { get; set; } = 14;

        [DisplayName("商品明细预留空白行数")]
        [Description("商品明细页在新建/清空后默认保留的空白录入行数")]
        public int ItemEntryBlankRowCount { get; set; } = 20;

        [DisplayName("审计日志保留天数")]
        [Description("审计日志（数据库 AuditLogs）保留天数，超过天数自动清理（默认180，0表示不清理）")]
        public int AuditLogRetentionDays { get; set; } = 180;

        [DisplayName("文本日志保留天数")]
        [Description("logs 目录中文本日志的保留天数，超过天数自动清理（默认30，0表示不清理）")]
        public int LogRetentionDays { get; set; } = 30;

        [DisplayName("文本日志保留文件数")]
        [Description("Serilog rolling 文件最大保留数量（默认14）")]
        public int LogRetainedFileCount { get; set; } = 14;

        [DisplayName("单个日志文件大小MB")]
        [Description("单个日志文件大小上限，超出后自动滚动（默认20MB）")]
        public int LogFileSizeLimitMB { get; set; } = 20;

        [DisplayName("文件导出默认保存路径")]
        [Description("Excel和PDF文件的默认导出目录")]
        public string DefaultExportDirectory { get; set; } = "";

        [DisplayName("数据库类型")]
        [Description("可选: Sqlite 或 PostgreSQL")]
        public string DatabaseProvider { get; set; } = "Sqlite";

        [DisplayName("SQLite 文件名")]
        [Description("当数据库类型为 Sqlite 时生效")]
        public string SqliteDatabaseFileName { get; set; } = "data.db";

        [DisplayName("PostgreSQL 服务器")]
        [Description("当数据库类型为 PostgreSQL 时生效，例如 127.0.0.1 或局域网服务器名")]
        public string PostgreSqlHost { get; set; } = "";

        [DisplayName("PostgreSQL 端口")]
        [Description("当数据库类型为 PostgreSQL 时生效，默认 5432")]
        public int PostgreSqlPort { get; set; } = 5432;

        [DisplayName("PostgreSQL 数据库名")]
        [Description("当数据库类型为 PostgreSQL 时生效")]
        public string PostgreSqlDatabase { get; set; } = "";

        [DisplayName("PostgreSQL 账号")]
        [Description("当数据库类型为 PostgreSQL 时生效")]
        public string PostgreSqlUsername { get; set; } = "";

        [DisplayName("PostgreSQL 密码")]
        [Description("当数据库类型为 PostgreSQL 时生效；如数据库允许空密码可留空")]
        [PasswordPropertyText(true)]
        public string PostgreSqlPassword { get; set; } = "";

        [DisplayName("PostgreSQL 附加参数")]
        [Description("当数据库类型为 PostgreSQL 时生效；用于 SSL、超时等高级参数，可留空")]
        public string PostgreSqlAdditionalOptions { get; set; } = "";

    }

    public class ExchangeRateSettings
    {
        [DisplayName("汇率源网址")]
        [Description("获取汇率的网页地址 (默认中国银行)")]
        public string Url { get; set; } = "https://www.boc.cn/sourcedb/whpj/";

        [DisplayName("缓存时间 (分钟)")]
        [Description("汇率数据的缓存时间，在此时间内不会重复请求")]
        public int CacheDurationMinutes { get; set; } = 30;

        [Browsable(false)]
        public List<string> SelectedCurrencies { get; set; } = new List<string>
        {
            "美元", "欧元", "日元", "英镑", "港币"
        };

        [Browsable(false)]
        public List<string> AllSupportedCurrencies { get; set; } = new List<string>
        {
            "美元", "欧元", "日元", "英镑", "港币", 
            "澳大利亚元", "加拿大元", "瑞士法郎", "新加坡元", 
            "新西兰元", "韩国元", "泰国铢", "卢布", "澳门元", "林吉特"
        };

        [Browsable(false)]
        public DateTime? LastCurrencyListUpdateTime { get; set; }
    }

    [TypeDescriptionProvider(typeof(OrderedTypeDescriptionProvider))]
    public class ExcelImportSettings
    {
        [Browsable(false)]
        public string SchemeName { get; set; } = "Default";

        // 01. Exporter
        [Category("01. 出口商信息 (Exporter)")]
        [DisplayName("出口商中文名称单元格")]
        [Description("出口商中文名称所在的Excel单元格位置，例如 A1")]
        [PropertyOrder(1)]
        public string ExporterNameCNCell { get; set; } = "A1";

        [Category("01. 出口商信息 (Exporter)")]
        [DisplayName("出口商英文名称单元格")]
        [Description("出口商英文名称所在的Excel单元格位置，例如 B3")]
        [PropertyOrder(2)]
        public string ExporterNameCell { get; set; } = "B3";

        [Category("01. 出口商信息 (Exporter)")]
        [DisplayName("出口商地址起始单元格")]
        [Description("出口商地址的起始单元格位置，通常包含多行，例如 B4")]
        [PropertyOrder(3)]
        public string ExporterAddressStartCell { get; set; } = "B4"; // B4-B7

        [Category("01. 出口商信息 (Exporter)")]
        [DisplayName("出口商地址行数")]
        [Description("出口商地址占用的行数")]
        [PropertyOrder(4)]
        public int ExporterAddressLineCount { get; set; } = 4;

        [Category("01. 出口商信息 (Exporter)")]
        [DisplayName("统一社会信用代码单元格")]
        [Description("统一社会信用代码所在的Excel单元格位置，例如 O4")]
        [PropertyOrder(5)]
        public string CreditCodeCell { get; set; } = "O4";


        // 02. Customer
        [Category("02. 客户信息 (Customer)")]
        [DisplayName("客户名称单元格")]
        [Description("客户名称所在的Excel单元格位置，例如 B8")]
        [PropertyOrder(11)]
        public string CustomerNameCell { get; set; } = "B8";

        [Category("02. 客户信息 (Customer)")]
        [DisplayName("客户地址起始单元格")]
        [Description("客户地址的起始单元格位置，通常包含多行，例如 B9")]
        [PropertyOrder(12)]
        public string CustomerAddressStartCell { get; set; } = "B9"; // B9-B12

        [Category("02. 客户信息 (Customer)")]
        [DisplayName("客户地址行数")]
        [Description("客户地址占用的行数")]
        [PropertyOrder(13)]
        public int CustomerAddressLineCount { get; set; } = 4;

        [Category("02. 客户信息 (Customer)")]
        [DisplayName("通知方名称单元格")]
        [Description("通知方名称所在的Excel单元格位置，例如 B13")]
        [PropertyOrder(14)]
        public string NotifyPartyNameCell { get; set; } = "B13";

        [Category("02. 客户信息 (Customer)")]
        [DisplayName("通知方地址起始单元格")]
        [Description("通知方地址的起始单元格位置，通常包含多行，例如 B14")]
        [PropertyOrder(15)]
        public string NotifyPartyAddressStartCell { get; set; } = "B14"; // B14-B17

        [Category("02. 客户信息 (Customer)")]
        [DisplayName("通知方地址行数")]
        [Description("通知方地址占用的行数")]
        [PropertyOrder(16)]
        public int NotifyPartyAddressLineCount { get; set; } = 4;


        // 03. Invoice
        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("发票日期单元格")]
        [Description("发票日期所在的Excel单元格位置，例如 O3")]
        [PropertyOrder(21)]
        public string InvoiceDateCell { get; set; } = "O3";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("合同号单元格")]
        [Description("合同号所在的Excel单元格位置，例如 O5")]
        [PropertyOrder(22)]
        public string ContractNoCell { get; set; } = "O5";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("开证行单元格")]
        [Description("开证行所在的Excel单元格位置，例如 O7")]
        [PropertyOrder(23)]
        public string IssuingBankCell { get; set; } = "O7";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("币种单元格")]
        [Description("币种所在的Excel单元格位置，例如 O8")]
        [PropertyOrder(24)]
        public string CurrencyCell { get; set; } = "O8";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("发票号单元格")]
        [Description("发票号所在的Excel单元格位置，例如 O9")]
        [PropertyOrder(25)]
        public string InvoiceNoCell { get; set; } = "O9";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("监管方式单元格")]
        [Description("监管方式所在的Excel单元格位置，例如 O10")]
        [PropertyOrder(26)]
        public string SupervisionModeCell { get; set; } = "O10";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("信用证号单元格")]
        [Description("信用证号所在的Excel单元格位置，例如 O6")]
        [PropertyOrder(27)]
        public string LetterOfCreditNoCell { get; set; } = "O6";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("付款方式单元格")]
        [Description("付款方式所在的Excel单元格位置，例如 O11")]
        [PropertyOrder(28)]
        public string PaymentTermsCell { get; set; } = "O11";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("运输方式单元格")]
        [Description("运输方式所在的Excel单元格位置，例如 O12")]
        [PropertyOrder(29)]
        public string TransportModeCell { get; set; } = "O12";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("贸易条款单元格")]
        [Description("贸易条款所在的Excel单元格位置，例如 O14")]
        [PropertyOrder(30)]
        public string TradeTermsCell { get; set; } = "O14";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("起运港单元格")]
        [Description("起运港所在的Excel单元格位置，例如 O15")]
        [PropertyOrder(31)]
        public string PortOfLoadingCell { get; set; } = "O15";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("目的港单元格")]
        [Description("目的港所在的Excel单元格位置，例如 O16")]
        [PropertyOrder(32)]
        public string PortOfDestinationCell { get; set; } = "O16";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("目的国单元格")]
        [Description("目的国所在的Excel单元格位置，例如 O17")]
        [PropertyOrder(33)]
        public string DestinationCountryCell { get; set; } = "O17";

        [Category("03. 发票信息 (Invoice)")]
        [DisplayName("唛头单元格")]
        [Description("唛头所在的Excel单元格位置，例如 A20")]
        [PropertyOrder(34)]
        public string ShippingMarksCell { get; set; } = "A20";


        // 04. Items Config
        [Category("04. 商品明细配置 (Items Config)")]
        [DisplayName("明细起始行")]
        [Description("商品明细数据开始的Excel行号，例如 20")]
        [PropertyOrder(41)]
        public int ItemsStartRow { get; set; } = 20;

        [Category("04. 商品明细配置 (Items Config)")]
        [DisplayName("明细结束行")]
        [Description("商品明细数据结束的Excel行号，0表示自动判断")]
        [PropertyOrder(42)]
        public int ItemsEndRow { get; set; } = 0;


        // 05. Items Columns
        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("订单号列")]
        [Description("订单号所在的列号 (1表示A列, 2表示B列...)")]
        [PropertyOrder(51)]
        public int PoNumberCol { get; set; } = 2;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("款号列")]
        [Description("款号所在的列号")]
        [PropertyOrder(52)]
        public int StyleNoCol { get; set; } = 3;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("品名英文列")]
        [Description("品名英文所在的列号")]
        [PropertyOrder(53)]
        public int StyleNameCol { get; set; } = 4;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("面料成分列")]
        [Description("面料成分所在的列号")]
        [PropertyOrder(54)]
        public int FabricCompositionCol { get; set; } = 5;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("品名中文列")]
        [Description("品名中文所在的列号")]
        [PropertyOrder(55)]
        public int StyleNameCNCol { get; set; } = 6;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("品牌列")]
        [Description("品牌所在的列号")]
        [PropertyOrder(56)]
        public int BrandCol { get; set; } = 7;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("HS编码列")]
        [Description("HS编码所在的列号")]
        [PropertyOrder(57)]
        public int HSCodeCol { get; set; } = 8;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("原产地列")]
        [Description("原产地所在的列号")]
        [PropertyOrder(58)]
        public int OriginCol { get; set; } = 9;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("数量列")]
        [Description("数量所在的列号")]
        [PropertyOrder(59)]
        public int QuantityCol { get; set; } = 10;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("单位英文列")]
        [Description("单位英文所在的列号")]
        [PropertyOrder(60)]
        public int UnitENCol { get; set; } = 11;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("单位中文列")]
        [Description("单位中文所在的列号")]
        [PropertyOrder(61)]
        public int UnitCNCol { get; set; } = 12;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("箱数列")]
        [Description("箱数所在的列号")]
        [PropertyOrder(62)]
        public int CartonsCol { get; set; } = 13;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("箱数单位英文列")]
        [Description("箱数单位英文所在的列号")]
        [PropertyOrder(63)]
        public int CtnUnitENCol { get; set; } = 14;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("长度列")]
        [Description("长度所在的列号")]
        [PropertyOrder(64)]
        public int LengthCol { get; set; } = 15;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("宽度列")]
        [Description("宽度所在的列号")]
        [PropertyOrder(65)]
        public int WidthCol { get; set; } = 16;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("高度列")]
        [Description("高度所在的列号")]
        [PropertyOrder(66)]
        public int HeightCol { get; set; } = 17;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("体积列")]
        [Description("体积所在的列号")]
        [PropertyOrder(67)]
        public int VolumeCol { get; set; } = 18;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("每箱毛重列")]
        [Description("每箱毛重所在的列号")]
        [PropertyOrder(68)]
        public int GWPerCtnCol { get; set; } = 19;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("总毛重列")]
        [Description("总毛重所在的列号")]
        [PropertyOrder(69)]
        public int GWTotalCol { get; set; } = 20;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("每箱净重列")]
        [Description("每箱净重所在的列号")]
        [PropertyOrder(70)]
        public int NWPerCtnCol { get; set; } = 21;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("总净重列")]
        [Description("总净重所在的列号")]
        [PropertyOrder(71)]
        public int NWTotalCol { get; set; } = 22;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("单价列")]
        [Description("单价所在的列号")]
        [PropertyOrder(72)]
        public int UnitPriceCol { get; set; } = 23;

        [Category("05. 商品明细列映射 (Items Columns)")]
        [DisplayName("总价列")]
        [Description("总价所在的列号")]
        [PropertyOrder(73)]
        public int TotalPriceCol { get; set; } = 24;
    }

    public class WebDavSettings
    {
        [Category("WebDAV 设置")]
        [DisplayName("服务器地址")]
        [Description("WebDAV 服务器地址，例如 https://dav.jianguoyun.com/dav/")]
        public string Url { get; set; }

        [Category("WebDAV 设置")]
        [DisplayName("用户名")]
        [Description("WebDAV 用户名")]
        public string UserName { get; set; }

        [Category("WebDAV 设置")]
        [DisplayName("密码/应用密码")]
        [Description("WebDAV 密码或应用专用密码")]
        [PasswordPropertyText(true)]
        public string Password { get; set; }

        [Category("WebDAV 设置")]
        [DisplayName("启用自动备份")]
        [Description("是否在退出软件时自动备份")]
        public bool Enabled { get; set; } = false;
    }
}
