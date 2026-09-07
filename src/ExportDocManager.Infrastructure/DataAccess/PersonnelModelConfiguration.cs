using ExportDocManager.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.DataAccess;

internal static class PersonnelModelConfiguration
{
    internal static void Configure(ModelBuilder model)
    {
        var people = model.Entity<PersonnelEmployee>();
        people.HasAlternateKey(item => new { item.Id, item.CompanyScope });
        people.HasIndex(item => new { item.CompanyScope, item.EmployeeNumberNormalized }).IsUnique();
        people.HasIndex(item => new { item.CompanyScope, item.RequestKey }).IsUnique();
        people.HasIndex(item => item.OwnerUserId).IsUnique();
        people.HasIndex(item => new { item.CompanyScope, item.Status, item.DepartmentId });
        people.Property(item => item.Status).HasConversion<string>().HasMaxLength(20);
        people.Property(item => item.EmploymentType).HasConversion<string>().HasMaxLength(20);
        model.Entity<OrganizationDepartment>().HasAlternateKey(item => new { item.Code, item.CompanyCode });
        people.HasOne(item => item.Department).WithMany().HasForeignKey(item => new { item.DepartmentId, item.CompanyScope })
            .HasPrincipalKey(item => new { item.Code, item.CompanyCode }).OnDelete(DeleteBehavior.Restrict);
        people.HasOne(item => item.Account).WithMany().HasForeignKey(item => item.OwnerUserId).OnDelete(DeleteBehavior.Restrict);
        people.ToTable("PersonnelEmployees", table => table.HasCheckConstraint("CK_Personnel_Employment",
            "\"Status\" IN ('Probation','Active','Departed') AND \"EmploymentType\" IN ('FullTime','PartTime','Intern','Contractor') " +
            "AND \"HireDate\" <= \"LastEffectiveDate\" AND (\"ProbationEndsOn\" IS NULL OR \"ProbationEndsOn\" >= \"HireDate\") " +
            "AND (\"ContractEndsOn\" IS NULL OR \"ContractEndsOn\" >= \"HireDate\") " +
            "AND ((\"Status\" = 'Departed' AND \"DepartedOn\" IS NOT NULL AND \"DepartedOn\" >= \"HireDate\") OR (\"Status\" <> 'Departed' AND \"DepartedOn\" IS NULL))"));

        var history = model.Entity<PersonnelEvent>();
        history.HasIndex(item => new { item.EmployeeId, item.Id });
        history.HasOne<PersonnelEmployee>().WithMany().HasForeignKey(item => new { item.EmployeeId, item.CompanyScope })
            .HasPrincipalKey(item => new { item.Id, item.CompanyScope }).OnDelete(DeleteBehavior.Restrict);
    }
}
