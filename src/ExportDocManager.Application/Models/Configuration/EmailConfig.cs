using System.ComponentModel;

namespace ExportDocManager.Models
{
    [TypeConverter(typeof(ExpandableObjectConverter))]
    public class EmailConfig
    {
        public const string DefaultDocumentEmailSubjectTemplate = "Export Documents for Invoice {InvoiceNo}";
        public const string DefaultDocumentEmailBodyTemplate = "Dear Customer,\r\n\r\nPlease find the attached export documents.\r\n\r\nBest regards,";

        [DisplayName("SMTP 服务器")]
        [Description("邮件服务器地址，例如 smtp.qq.com")]
        public string SmtpHost { get; set; }

        [DisplayName("SMTP 端口")]
        [Description("邮件服务器端口，通常为 587 (TLS) 或 465 (SSL)")]
        public int SmtpPort { get; set; } = 587;

        [DisplayName("用户名/邮箱")]
        [Description("登录邮箱的用户名或完整邮箱地址")]
        public string UserName { get; set; }

        [DisplayName("密码/授权码")]
        [Description("邮箱密码或应用授权码 (推荐使用授权码)")]
        [PasswordPropertyText(true)]
        public string Password { get; set; }

        [DisplayName("启用 SSL")]
        [Description("是否启用安全连接 (SSL/TLS)")]
        public bool EnableSsl { get; set; } = true;

        [DisplayName("发件人地址")]
        [Description("邮件显示的发送方地址")]
        public string FromAddress { get; set; }

        [DisplayName("发件人名称")]
        [Description("邮件显示的发送方名称，例如 '单证部'")]
        public string FromDisplayName { get; set; }

        [DisplayName("单据邮件默认主题")]
        [Description("从发票页发送单据附件时的默认主题，支持占位符：{InvoiceNo} {Customer} {Date}")]
        public string DocumentEmailSubjectTemplate { get; set; } = DefaultDocumentEmailSubjectTemplate;

        [DisplayName("单据邮件默认正文")]
        [Description("从发票页发送单据附件时的默认正文，支持占位符：{InvoiceNo} {Customer} {Date}")]
        public string DocumentEmailBodyTemplate { get; set; } = DefaultDocumentEmailBodyTemplate;
    }
}
