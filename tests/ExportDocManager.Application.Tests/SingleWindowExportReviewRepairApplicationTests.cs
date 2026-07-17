using ExportDocManager.Models.Entities;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowExportReviewRepairApplicationTests
    {
        [Fact]
        public void RepairCustomsCooGroups_ShouldApplyHeaderDefaultsAndFallbacksWithoutWinFormsCatalog()
        {
            var document = new CustomsCooDocument
            {
                ApplyType = "9",
                CertStatus = "9",
                CertType = "X",
                AplPromiseCode = "9",
                EtpsName = "OLD"
            };
            var defaults = new CustomsCooDocument
            {
                ApplyType = " ",
                CertStatus = "",
                CertType = null,
                AplPromiseCode = "",
                EtpsName = "  EXPORTER LTD.  "
            };

            int repairedGroups = SingleWindowExportReviewRepairHelper.RepairCustomsCooGroups(
                document,
                defaults,
                ["证书基础", "补充与特殊项"]);

            Assert.Equal(2, repairedGroups);
            Assert.Equal("0", document.ApplyType);
            Assert.Equal("0", document.CertStatus);
            Assert.Equal("C", document.CertType);
            Assert.Equal("1", document.AplPromiseCode);
            Assert.Equal("EXPORTER LTD.", document.EtpsName);
        }

        [Fact]
        public void RepairCustomsCooGroups_ShouldRepairGoodsAndClearAttachmentFields()
        {
            var document = new CustomsCooDocument
            {
                Items =
                [
                    new CustomsCooItem
                    {
                        GNo = 1,
                        GoodsName = "OLD",
                        PackType = "9",
                        HSCode = "OLD-HS"
                    }
                ],
                Attachments =
                [
                    new CustomsCooAttachment
                    {
                        CertType = "C",
                        AplRegNo = "APL",
                        Description = "delay",
                        IsDelay = true
                    }
                ]
            };
            var defaults = new CustomsCooDocument
            {
                Items =
                [
                    new CustomsCooItem
                    {
                        GNo = 1,
                        GoodsName = "  POWER SUPPLY  ",
                        PackType = "1",
                        HSCode = "850440"
                    }
                ]
            };

            int repairedGroups = SingleWindowExportReviewRepairHelper.RepairCustomsCooGroups(
                document,
                defaults,
                ["明细项目", "附件"]);

            Assert.Equal(2, repairedGroups);
            Assert.Equal("POWER SUPPLY", document.Items[0].GoodsName);
            Assert.Equal("1", document.Items[0].PackType);
            Assert.Equal("850440", document.Items[0].HSCode);
            Assert.Empty(document.Attachments[0].CertType);
            Assert.Empty(document.Attachments[0].AplRegNo);
            Assert.Empty(document.Attachments[0].Description);
            Assert.False(document.Attachments[0].IsDelay);
        }

        [Fact]
        public void RepairAgentConsignmentGroups_ShouldApplyDefaultsAndIgnoreReceiptGroup()
        {
            var document = new AgentConsignmentDocument
            {
                CopCusCode = "OLD",
                OperType = "9",
                Sign = "OLD-SIGN",
                GName = "OLD-GOODS",
                CodeTS = "OLD-HS",
                ConsignNo = "KEEP"
            };
            var defaults = new AgentConsignmentDocument
            {
                CopCusCode = "3302961234",
                OperType = "",
                Sign = "NEW-SIGN",
                GName = "  POWER SUPPLY  ",
                CodeTS = "850440",
                ConsignNo = "NEW-CONSIGN"
            };

            int repairedGroups = SingleWindowExportReviewRepairHelper.RepairAgentConsignmentGroups(
                document,
                defaults,
                [
                    SingleWindowExportReviewRepairCatalog.AgentConsignmentDefaultSectionKey,
                    SingleWindowExportReviewRepairCatalog.AgentConsignmentReceiptSectionKey
                ]);

            Assert.Equal(1, repairedGroups);
            Assert.Equal("3302961234", document.CopCusCode);
            Assert.Equal("1", document.OperType);
            Assert.Equal("NEW-SIGN", document.Sign);
            Assert.Equal("POWER SUPPLY", document.GName);
            Assert.Equal("850440", document.CodeTS);
            Assert.Equal("KEEP", document.ConsignNo);
        }
    }
}
