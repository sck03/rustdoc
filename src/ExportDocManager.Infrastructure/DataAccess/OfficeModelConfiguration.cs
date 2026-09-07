using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.DataAccess;

internal static class OfficeModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        var rooms = model.Entity<MeetingRoom>();
        rooms.HasAlternateKey(item => new { item.Id, item.CompanyScope });
        rooms.HasIndex(item => new { item.CompanyScope, item.NameNormalized }).IsUnique();
        rooms.HasOne<OrganizationCompany>().WithMany().HasForeignKey(item => item.CompanyScope).OnDelete(DeleteBehavior.Restrict);
        rooms.ToTable("MeetingRooms", table => table.HasCheckConstraint("CK_MeetingRoom_Limits",
            "\"Capacity\" BETWEEN 1 AND 10000 AND \"MaximumBookingHours\" BETWEEN 1 AND 24 AND \"AdvanceBookingDays\" BETWEEN 1 AND 365"));

        var bookings = model.Entity<MeetingBooking>();
        bookings.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
        bookings.HasAlternateKey(item => new { item.Id, item.CompanyScope });
        bookings.HasIndex(item => new { item.CompanyScope, item.OwnerUserId, item.RequestKey }).IsUnique();
        bookings.HasIndex(item => new { item.MeetingRoomId, item.Status, item.StartsAt, item.EndsAt });
        bookings.HasIndex(item => new { item.CompanyScope, item.Status, item.CreatedAt });
        bookings.HasIndex(item => new { item.EmployeeId, item.Status });
        bookings.HasOne<PersonnelEmployee>().WithMany().HasForeignKey(item => new { item.EmployeeId, item.CompanyScope })
            .HasPrincipalKey(item => new { item.Id, item.CompanyScope }).OnDelete(DeleteBehavior.Restrict);
        bookings.HasOne<MeetingRoom>().WithMany().HasForeignKey(item => new { item.MeetingRoomId, item.CompanyScope })
            .HasPrincipalKey(item => new { item.Id, item.CompanyScope }).OnDelete(DeleteBehavior.Restrict);
        bookings.ToTable("MeetingBookings", table => table.HasCheckConstraint("CK_MeetingBooking_Valid",
            "\"StartsAt\" < \"EndsAt\" AND \"AttendeeCount\" > 0 AND \"OwnerUserId\" IS NOT NULL AND \"Status\" IN ('Pending','Approved','InUse','Completed','Rejected','Cancelled')"));

        var supplies = model.Entity<OfficeSupply>();
        supplies.HasAlternateKey(item => new { item.Id, item.CompanyScope });
        supplies.HasIndex(item => new { item.CompanyScope, item.NameNormalized }).IsUnique();
        supplies.HasOne<OrganizationCompany>().WithMany().HasForeignKey(item => item.CompanyScope).OnDelete(DeleteBehavior.Restrict);
        supplies.ToTable("OfficeSupplies", table => table.HasCheckConstraint("CK_OfficeSupply_Stock",
            "\"StockQuantity\" BETWEEN 0 AND 1000000 AND \"ReservedQuantity\" BETWEEN 0 AND \"StockQuantity\" AND \"MinimumStock\" BETWEEN 0 AND 1000000"));

        var requests = model.Entity<OfficeSupplyRequest>();
        requests.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
        requests.HasAlternateKey(item => new { item.Id, item.CompanyScope });
        requests.HasIndex(item => new { item.CompanyScope, item.OwnerUserId, item.RequestKey }).IsUnique();
        requests.HasIndex(item => new { item.CompanyScope, item.Status, item.CreatedAt });
        requests.HasIndex(item => new { item.EmployeeId, item.Status });
        requests.HasOne<PersonnelEmployee>().WithMany().HasForeignKey(item => new { item.EmployeeId, item.CompanyScope })
            .HasPrincipalKey(item => new { item.Id, item.CompanyScope }).OnDelete(DeleteBehavior.Restrict);
        requests.HasOne<OfficeSupply>().WithMany().HasForeignKey(item => new { item.OfficeSupplyId, item.CompanyScope })
            .HasPrincipalKey(item => new { item.Id, item.CompanyScope }).OnDelete(DeleteBehavior.Restrict);
        requests.ToTable("OfficeSupplyRequests", table => table.HasCheckConstraint("CK_OfficeSupplyRequest_Quantity",
            "\"Quantity\" BETWEEN 1 AND 1000000 AND \"ReturnedQuantity\" BETWEEN 0 AND \"Quantity\" AND \"OwnerUserId\" IS NOT NULL AND \"Status\" IN ('Pending','Approved','Issued','Returned','Rejected','Cancelled')"));

        var events = model.Entity<OfficeRequestEvent>();
        events.HasOne<MeetingBooking>().WithMany().HasForeignKey(item => new { item.MeetingBookingId, item.CompanyScope })
            .HasPrincipalKey(item => new { item.Id, item.CompanyScope }).OnDelete(DeleteBehavior.Restrict);
        events.HasOne<OfficeSupplyRequest>().WithMany().HasForeignKey(item => new { item.OfficeSupplyRequestId, item.CompanyScope })
            .HasPrincipalKey(item => new { item.Id, item.CompanyScope }).OnDelete(DeleteBehavior.Restrict);
        events.ToTable("OfficeRequestEvents", table => table.HasCheckConstraint("CK_OfficeRequestEvent_Owner",
            "(\"MeetingBookingId\" IS NOT NULL AND \"OfficeSupplyRequestId\" IS NULL) OR (\"MeetingBookingId\" IS NULL AND \"OfficeSupplyRequestId\" IS NOT NULL)"));

        var movements = model.Entity<OfficeStockMovement>();
        movements.HasIndex(item => new { item.CompanyScope, item.OperationId }).IsUnique();
        movements.HasIndex(item => new { item.OfficeSupplyId, item.CreatedAt });
        movements.HasOne<OfficeSupply>().WithMany().HasForeignKey(item => new { item.OfficeSupplyId, item.CompanyScope })
            .HasPrincipalKey(item => new { item.Id, item.CompanyScope }).OnDelete(DeleteBehavior.Restrict);
        movements.HasOne<OfficeSupplyRequest>().WithMany().HasForeignKey(item => new { item.OfficeSupplyRequestId, item.CompanyScope })
            .HasPrincipalKey(item => new { item.Id, item.CompanyScope }).OnDelete(DeleteBehavior.Restrict);
    }
}
