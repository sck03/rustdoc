using ExportDocManager.Models.Entities;
using ExportDocManager.Services.SingleWindow;

namespace ExportDocManager.Application.Tests
{
    public class SingleWindowDraftStateApplicationTests
    {
        [Fact]
        public void BuildCustomsCooLockedOverlay_ShouldKeepLockedFieldsAndClearUnlockedValues()
        {
            string goodsIdentity = SingleWindowDraftStateHelper.GetGoodsIdentity(0, null, 1);
            var stored = new CustomsCooDocument
            {
                Producer = "MANUAL PRODUCER",
                DestCountry = "ID",
                ManualLockedFieldsJson = SingleWindowDraftStateHelper.SerializeLockedFields(
                [
                    nameof(CustomsCooDocument.Producer),
                    $"Goods:{goodsIdentity}:{nameof(CustomsCooItem.GoodsNameE)}"
                ]),
                Items =
                [
                    new CustomsCooItem
                    {
                        GNo = 1,
                        GoodsNameE = "MANUAL GOODS",
                        HSCode = "850440"
                    }
                ]
            };

            var overlay = SingleWindowDraftStateHelper.BuildCustomsCooLockedOverlay(
                stored,
                [new Item()]);

            Assert.Equal("MANUAL PRODUCER", overlay.Producer);
            Assert.Equal(string.Empty, overlay.DestCountry);
            var item = Assert.Single(overlay.Items);
            Assert.Equal("MANUAL GOODS", item.GoodsNameE);
            Assert.Equal(string.Empty, item.HSCode);
            Assert.Equal("850440", Assert.Single(stored.Items).HSCode);
        }

        [Fact]
        public void CloneCustomsCooDocument_ShouldDeepCloneCollections()
        {
            var source = new CustomsCooDocument
            {
                Producer = "SOURCE PRODUCER",
                Items =
                [
                    new CustomsCooItem
                    {
                        GoodsItemFlag = " invalid ",
                        GoodsNameE = "SOURCE GOODS"
                    }
                ],
                Attachments =
                [
                    new CustomsCooAttachment
                    {
                        FileName = "invoice.pdf",
                        SortOrder = 2
                    }
                ]
            };

            var clone = SingleWindowSourceCloneHelper.CloneCustomsCooDocument(source);

            Assert.NotSame(source, clone);
            Assert.NotSame(source.Items, clone.Items);
            Assert.NotSame(Assert.Single(source.Items), Assert.Single(clone.Items));
            Assert.Equal("N", Assert.Single(clone.Items).GoodsItemFlag);

            clone.Items[0].GoodsNameE = "CHANGED";
            clone.Attachments[0].FileName = "changed.pdf";

            Assert.Equal("SOURCE GOODS", source.Items[0].GoodsNameE);
            Assert.Equal("invoice.pdf", source.Attachments[0].FileName);
        }
    }
}
