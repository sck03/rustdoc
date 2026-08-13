using ExportDocManager.Models.Entities;

namespace ExportDocManager.Domain.Tests
{
    public sealed class NotifyPartyModePolicyTests
    {
        [Fact]
        public void Normalize_ShouldKeepSeparatePartyAndClearNonSeparateStorage()
        {
            var separate = new Invoice
            {
                NotifyPartyMode = NotifyPartyMode.Separate,
                NotifyPartyName = "Notify Ltd.",
                NotifyPartyAddress = "2 Notify Road"
            };
            var sameAsConsignee = new Invoice
            {
                NotifyPartyMode = NotifyPartyMode.SameAsConsignee,
                NotifyPartyName = "stale copy",
                NotifyPartyAddress = "stale address"
            };

            NotifyPartyModePolicy.Normalize(separate);
            NotifyPartyModePolicy.Normalize(sameAsConsignee);

            Assert.Equal("Notify Ltd.", separate.NotifyPartyName);
            Assert.Equal("2 Notify Road", separate.NotifyPartyAddress);
            Assert.Equal(string.Empty, sameAsConsignee.NotifyPartyName);
            Assert.Equal(string.Empty, sameAsConsignee.NotifyPartyAddress);
        }

        [Theory]
        [InlineData(NotifyPartyMode.None, "", "")]
        [InlineData(NotifyPartyMode.SameAsConsignee, "Buyer Ltd.", "1 Buyer Road")]
        [InlineData(NotifyPartyMode.Separate, "Notify Ltd.", "2 Notify Road")]
        public void ResolveForDocument_ShouldProjectModeWithoutCreatingStoredCopies(
            NotifyPartyMode mode,
            string expectedName,
            string expectedAddress)
        {
            var resolved = NotifyPartyModePolicy.ResolveForDocument(
                mode,
                "Buyer Ltd.",
                "1 Buyer Road",
                "Notify Ltd.",
                "2 Notify Road");

            Assert.Equal(expectedName, resolved.Name);
            Assert.Equal(expectedAddress, resolved.Address);
        }

        [Fact]
        public void CloneHeader_ShouldNeverCopyNonSeparateNotifyPartyFields()
        {
            var source = new Invoice
            {
                NotifyPartyMode = NotifyPartyMode.SameAsConsignee,
                NotifyPartyName = "stale copy",
                NotifyPartyAddress = "stale address"
            };

            var clone = source.CloneHeader();

            Assert.Equal(NotifyPartyMode.SameAsConsignee, clone.NotifyPartyMode);
            Assert.Equal(string.Empty, clone.NotifyPartyName);
            Assert.Equal(string.Empty, clone.NotifyPartyAddress);
        }
    }
}
