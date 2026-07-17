using ExportDocManager.Api.Hosting;
using ExportDocManager.Models;
using ExportDocManager.Models.Entities;

namespace ExportDocManager.Api.Tests
{
    public class ApiAuditLogTests
    {
        [Fact]
        public void AuditLogDtoFactory_ShouldMapPagedAuditLogs()
        {
            string oldValues = new string('A', 190).Insert(10, "\n");
            var timestamp = new DateTime(2026, 6, 23, 8, 30, 0, DateTimeKind.Utc);
            var result = new PagedResult<AuditLog>(
                new List<AuditLog>
                {
                    new()
                    {
                        Id = 9,
                        EntityName = "Invoice",
                        Action = "Update",
                        EntityId = "42",
                        OldValues = oldValues,
                        NewValues = "{\"InvoiceNo\":\"INV-1\"}",
                        UserId = "admin",
                        Timestamp = timestamp
                    }
                },
                totalCount: 3,
                pageNumber: 2,
                pageSize: 1);

            var dto = ApiAuditLogDtoFactory.FromPagedAuditLogs(result);
            var item = Assert.Single(dto.Items);

            Assert.Equal(3, dto.TotalCount);
            Assert.Equal(2, dto.PageNumber);
            Assert.Equal(1, dto.PageSize);
            Assert.Equal(9, item.Id);
            Assert.Equal("Invoice", item.EntityName);
            Assert.Equal("Update", item.Action);
            Assert.Equal("42", item.EntityId);
            Assert.Equal(oldValues, item.OldValues);
            Assert.Equal("{\"InvoiceNo\":\"INV-1\"}", item.NewValues);
            Assert.Equal("admin", item.UserId);
            Assert.Equal(timestamp, item.Timestamp);
            Assert.DoesNotContain("\n", item.OldValuesPreview, StringComparison.Ordinal);
            Assert.EndsWith("...", item.OldValuesPreview, StringComparison.Ordinal);
        }

        [Fact]
        public void AuditLogDtoFactory_ShouldNormalizeNullableTextFields()
        {
            var dto = ApiAuditLogDtoFactory.FromAuditLog(new AuditLog
            {
                Timestamp = DateTime.UtcNow
            });

            Assert.Equal(string.Empty, dto.EntityName);
            Assert.Equal(string.Empty, dto.Action);
            Assert.Equal(string.Empty, dto.EntityId);
            Assert.Equal(string.Empty, dto.OldValues);
            Assert.Equal(string.Empty, dto.NewValues);
            Assert.Equal(string.Empty, dto.UserId);
            Assert.Equal(string.Empty, dto.OldValuesPreview);
            Assert.Equal(string.Empty, dto.NewValuesPreview);
        }
    }
}
