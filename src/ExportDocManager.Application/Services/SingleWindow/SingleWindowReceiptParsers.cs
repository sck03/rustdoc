using System.Xml.Linq;
using ExportDocManager.Models.DTOs.SingleWindow;

namespace ExportDocManager.Services.SingleWindow
{
    public interface ISingleWindowReceiptParser
    {
        SingleWindowReceiptParseResult Parse(SingleWindowBusinessType businessType, string rawContent, string sourceFileName = "");
    }

    public sealed class SingleWindowReceiptParser : ISingleWindowReceiptParser
    {
        public SingleWindowReceiptParseResult Parse(SingleWindowBusinessType businessType, string rawContent, string sourceFileName = "")
        {
            if (string.IsNullOrWhiteSpace(rawContent))
            {
                return new SingleWindowReceiptParseResult
                {
                    BusinessType = businessType,
                    ReceiptKind = SingleWindowReceiptKind.Unknown,
                    ReceiptMessage = "回执内容为空。",
                    BusinessStatus = SingleWindowReceiptBusinessStatus.Unknown,
                    SourceFileName = sourceFileName
                };
            }

            var document = XDocument.Parse(rawContent);
            var root = document.Root;
            if (root == null)
            {
                return new SingleWindowReceiptParseResult
                {
                    BusinessType = businessType,
                    ReceiptKind = SingleWindowReceiptKind.Unknown,
                    ReceiptMessage = "回执缺少根节点。",
                    BusinessStatus = SingleWindowReceiptBusinessStatus.Unknown,
                    SourceFileName = sourceFileName
                };
            }

            return businessType switch
            {
                SingleWindowBusinessType.CustomsCoo => ParseCustomsCoo(root, sourceFileName),
                SingleWindowBusinessType.AgentConsignment => ParseAgentConsignment(root, sourceFileName),
                _ => new SingleWindowReceiptParseResult
                {
                    BusinessType = businessType,
                    ReceiptKind = SingleWindowReceiptKind.Unknown,
                    ReceiptMessage = "未知业务类型。",
                    BusinessStatus = SingleWindowReceiptBusinessStatus.Unknown,
                    SourceFileName = sourceFileName
                }
            };
        }

        private static SingleWindowReceiptParseResult ParseCustomsCoo(XElement root, string sourceFileName)
        {
            if (string.Equals(root.Name.LocalName, "Receipt", StringComparison.Ordinal))
            {
                var channel = ReadValue(root, "Channel");
                var receiptCode = ReadValue(root, "RepCode");
                var receiptMessage = ReadValue(root, "RepAddMsg");
                if (string.IsNullOrWhiteSpace(receiptMessage))
                {
                    receiptMessage = ReadValue(root, "Note");
                }

                return new SingleWindowReceiptParseResult
                {
                    BusinessType = SingleWindowBusinessType.CustomsCoo,
                    ReceiptKind = SingleWindowReceiptKind.CustomsCooBusinessReceipt,
                    ReferenceNo = ReadValue(root, "CertNo"),
                    ReceiptCode = receiptCode,
                    ReceiptMessage = receiptMessage,
                    BusinessStatus = ParseCustomsCooBusinessStatus(channel, ReadValue(root, "RepType")),
                    OccurredAt = ParseDateTime(ReadValue(root, "RspGenTime"))
                        ?? ParseDateTime(ReadValue(root, "ReceiveTime"))
                        ?? ParseDateTime(ReadValue(root, "SendTime")),
                    SourceFileName = sourceFileName
                };
            }

            if (string.Equals(root.Name.LocalName, "FileRet", StringComparison.Ordinal))
            {
                bool hasAttachmentFields =
                    !string.IsNullOrWhiteSpace(ReadValue(root, "FileName")) ||
                    !string.IsNullOrWhiteSpace(ReadValue(root, "FileType"));

                return new SingleWindowReceiptParseResult
                {
                    BusinessType = SingleWindowBusinessType.CustomsCoo,
                    ReceiptKind = hasAttachmentFields
                        ? SingleWindowReceiptKind.CustomsCooAttachmentReceipt
                        : SingleWindowReceiptKind.CustomsCooTechnicalReceipt,
                    ReferenceNo = ReadValue(root, "CertNo"),
                    ReceiptCode = ReadValue(root, "RetType"),
                    ReceiptMessage = ReadValue(root, "Note"),
                    BusinessStatus = ParseFileRetStatus(ReadValue(root, "RetType")),
                    OccurredAt = ParseDateTime(ReadValue(root, "ReceiveTime"))
                        ?? ParseDateTime(ReadValue(root, "SendTime")),
                    SourceFileName = sourceFileName
                };
            }

            return new SingleWindowReceiptParseResult
            {
                BusinessType = SingleWindowBusinessType.CustomsCoo,
                ReceiptKind = SingleWindowReceiptKind.Unknown,
                ReceiptMessage = $"未知的海关原产地证回执根节点: {root.Name.LocalName}",
                BusinessStatus = SingleWindowReceiptBusinessStatus.Unknown,
                SourceFileName = sourceFileName
            };
        }

        private static SingleWindowReceiptParseResult ParseAgentConsignment(XElement root, string sourceFileName)
        {
            if (string.Equals(root.Name.LocalName, "ImportAgrResponse", StringComparison.Ordinal))
            {
                var responseCode = ReadValue(root, "ResponseCode");
                return new SingleWindowReceiptParseResult
                {
                    BusinessType = SingleWindowBusinessType.AgentConsignment,
                    ReceiptKind = SingleWindowReceiptKind.AgentConsignmentImportResponse,
                    ReferenceNo = ReadValue(root, "ConsignNo"),
                    ReceiptCode = responseCode,
                    ReceiptMessage = ReadValue(root, "ResponseMessage"),
                    BusinessStatus = string.Equals(responseCode, "0", StringComparison.Ordinal)
                        ? SingleWindowReceiptBusinessStatus.Accepted
                        : SingleWindowReceiptBusinessStatus.Rejected,
                    SourceFileName = sourceFileName
                };
            }

            if (string.Equals(root.Name.LocalName, "Signature", StringComparison.Ordinal))
            {
                var procResult = ReadValue(root, "PROC_RESULT");
                return new SingleWindowReceiptParseResult
                {
                    BusinessType = SingleWindowBusinessType.AgentConsignment,
                    ReceiptKind = SingleWindowReceiptKind.AgentConsignmentAcd002,
                    ReferenceNo = ReadValue(root, "CONSIGN_NO"),
                    ReceiptCode = procResult,
                    ReceiptMessage = ReadValue(root, "PROC_DESC"),
                    BusinessStatus = string.Equals(procResult, "S", StringComparison.OrdinalIgnoreCase)
                        ? SingleWindowReceiptBusinessStatus.Accepted
                        : SingleWindowReceiptBusinessStatus.Rejected,
                    OccurredAt = ParseDateTime(ReadValue(root, "OP_TIME"))
                        ?? ParseDateTime(ReadValue(root, "send_time")),
                    SourceFileName = sourceFileName
                };
            }

            return new SingleWindowReceiptParseResult
            {
                BusinessType = SingleWindowBusinessType.AgentConsignment,
                ReceiptKind = SingleWindowReceiptKind.Unknown,
                ReceiptMessage = $"未知的报关代理委托回执根节点: {root.Name.LocalName}",
                BusinessStatus = SingleWindowReceiptBusinessStatus.Unknown,
                SourceFileName = sourceFileName
            };
        }

        private static string ReadValue(XElement root, string localName)
        {
            return root
                .Descendants()
                .FirstOrDefault(element => string.Equals(element.Name.LocalName, localName, StringComparison.Ordinal))
                ?.Value
                ?.Trim() ?? string.Empty;
        }

        private static DateTime? ParseDateTime(string value)
        {
            return DateTime.TryParse(value, out var result) ? result : null;
        }

        private static SingleWindowReceiptBusinessStatus ParseCustomsCooBusinessStatus(string channel, string repType)
        {
            if (string.Equals(channel, "1", StringComparison.Ordinal))
            {
                return SingleWindowReceiptBusinessStatus.Received;
            }

            if (string.Equals(channel, "2", StringComparison.Ordinal))
            {
                return SingleWindowReceiptBusinessStatus.Failed;
            }

            return repType switch
            {
                "2" => SingleWindowReceiptBusinessStatus.PendingReview,
                "5" => SingleWindowReceiptBusinessStatus.Approved,
                "3" or "6" => SingleWindowReceiptBusinessStatus.Rejected,
                "1" => SingleWindowReceiptBusinessStatus.Failed,
                _ => SingleWindowReceiptBusinessStatus.Unknown
            };
        }

        private static SingleWindowReceiptBusinessStatus ParseFileRetStatus(string retType)
        {
            return retType switch
            {
                "1" or "3" => SingleWindowReceiptBusinessStatus.Accepted,
                "2" or "4" => SingleWindowReceiptBusinessStatus.Rejected,
                _ => SingleWindowReceiptBusinessStatus.Unknown
            };
        }
    }
}
