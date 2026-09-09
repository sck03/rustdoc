using System.Buffers.Binary;
using System.Text.Json;
using ExportDocManager.DataAccess;
using ExportDocManager.Models.Entities;
using ExportDocManager.Services.Errors;
using ExportDocManager.Services.Office;
using ExportDocManager.Services.Security;
using Microsoft.EntityFrameworkCore;

namespace ExportDocManager.Infrastructure.Tests;

public sealed class PersonnelIdentityAndImageTests
{
    // Publicly documented checksum example, never a customer record.
    private const string ExampleIdentity = "11010519491231002X";
    private static byte[] Png => Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+j4i8AAAAASUVORK5CYII=");

    [Fact]
    public async Task Identity_ShouldRoundTripPrivately_AndOnlyLogChangedFieldNames()
    {
        using var env = new PersonnelTestEnvironment();
        var request = env.Request();
        var profile = request.Profile with
        {
            IdentityNumber = ExampleIdentity.ToLowerInvariant(),
            IdentityAuthority = "示例签发机关",
            RegisteredAddress = "测试地址",
            IdentityValidFrom = new(2020, 1, 1),
            IdentityValidUntil = new(2030, 1, 1)
        };
        var person = await env.People.CreateAsync(request with { Profile = profile });
        Assert.Equal(ExampleIdentity, person.Profile.IdentityNumber);
        Assert.DoesNotContain("Identity", JsonSerializer.Serialize(await env.People.QueryAsync(new())));
        var changed = await env.People.UpdateAsync(person.Employee.Id, new(person.VersionNumber,
            person.Profile with { IdentityValidUntil = null, IdentityLongTerm = true }, person.EmploymentType, person.ProbationEndsOn, person.ContractEndsOn));
        Assert.True(changed.Record.Profile.IdentityLongTerm);
        Assert.Null(changed.Record.Profile.IdentityValidUntil);
        var history = JsonSerializer.Serialize(await env.People.HistoryAsync(person.Employee.Id, 1, 24));
        Assert.DoesNotContain(ExampleIdentity, history);
        Assert.Equal("[REDACTED]", AuditValuePolicy.Sanitize("IdentityNumber", ExampleIdentity));
        Assert.Equal("[REDACTED]", AuditValuePolicy.Sanitize("IdentityValidFrom", profile.IdentityValidFrom));
        Assert.Equal("[REDACTED]", AuditValuePolicy.Sanitize("RegisteredAddress", "测试地址"));
        env.AsEmployee();
        Assert.DoesNotContain(ExampleIdentity, JsonSerializer.Serialize(await env.People.QueryAsync(new())));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.GetAsync(person.Employee.Id));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("110105194912310020")]
    [InlineData("11010519490231002X")]
    [InlineData("11010529991231002X")]
    [InlineData("11010519491231000X")]
    [InlineData("11010519491231 02X")]
    public async Task InvalidIdentity_ShouldRejectTheEntireHire(string number)
    {
        using var env = new PersonnelTestEnvironment();
        var request = env.Request();
        await Assert.ThrowsAsync<ServiceValidationException>(() => env.People.CreateAsync(request with { Profile = request.Profile with { IdentityNumber = number } }));
        Assert.Empty((await env.People.QueryAsync(new())).Items);
    }

    [Fact]
    public async Task Validity_ShouldRejectPartialOrContradictoryDates_AndParticipateInAttentionFilter()
    {
        using var env = new PersonnelTestEnvironment();
        var request = env.Request() with { OnProbation = false, ProbationEndsOn = null, ContractEndsOn = null };
        var profile = request.Profile with { IdentityNumber = ExampleIdentity, IdentityValidFrom = new(2020, 1, 1) };
        foreach (var invalid in new[] { profile, profile with { IdentityLongTerm = true, IdentityValidUntil = new(2030, 1, 1) },
            profile with { IdentityValidUntil = new(2019, 1, 1) }, profile with { IdentityValidFrom = env.Today.AddDays(1), IdentityLongTerm = true } })
            await Assert.ThrowsAsync<ServiceValidationException>(() => env.People.CreateAsync(request with { Profile = invalid }));
        var person = await env.People.CreateAsync(request with { Profile = profile with { IdentityValidUntil = env.Today.AddDays(15) } });
        Assert.Equal(person.Employee.Id, Assert.Single((await env.People.QueryAsync(new(AttentionOnly: true))).Items).Id);
    }

    [Fact]
    public async Task Images_ShouldPersistSeparately_RespectPrivateScope_AndUseEmployeeConcurrency()
    {
        using var env = new PersonnelTestEnvironment();
        var person = await env.People.CreateAsync(env.Request());
        int oldVersion = person.VersionNumber;
        using var source = new MemoryStream(Png);
        person = await env.People.SaveImageAsync(person.Employee.Id, PersonnelImageKind.Avatar, person.VersionNumber, source, "image/png");
        Assert.True(person.VersionNumber > oldVersion);
        Assert.NotNull(person.Employee.AvatarHash);
        Assert.Single(person.Images);
        await using (var db = env.Database.CreateDbContext()) Assert.Equal(Png, (await db.PersonnelImages.SingleAsync()).Content);
        using var identity = new MemoryStream(Png);
        person = await env.People.SaveImageAsync(person.Employee.Id, PersonnelImageKind.IdentityFront, person.VersionNumber, identity, "image/png");
        env.AsEmployee();
        Assert.Equal(Png, (await env.People.ReadImageAsync(person.Employee.Id, PersonnelImageKind.Avatar)).Content);
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.ReadImageAsync(person.Employee.Id, PersonnelImageKind.IdentityFront));
        await Assert.ThrowsAsync<PermissionDeniedException>(() => env.People.DeleteImageAsync(person.Employee.Id, PersonnelImageKind.Avatar, person.VersionNumber));
        var directory = await env.People.QueryAsync(new());
        Assert.NotNull(Assert.Single(directory.Items).AvatarHash);
        Assert.DoesNotContain("IdentityFront", JsonSerializer.Serialize(directory));
        env.AsAdmin();
        await Assert.ThrowsAsync<ServiceConcurrencyException>(() => env.People.DeleteImageAsync(person.Employee.Id, PersonnelImageKind.Avatar, oldVersion));
        person = await env.People.DeleteImageAsync(person.Employee.Id, PersonnelImageKind.Avatar, person.VersionNumber);
        Assert.Null(person.Employee.AvatarHash);
        Assert.Equal(PersonnelImageKind.IdentityFront, Assert.Single(person.Images).Kind);
        int deletedVersion = person.VersionNumber;
        Assert.Equal(deletedVersion, (await env.People.DeleteImageAsync(person.Employee.Id, PersonnelImageKind.Avatar, deletedVersion)).VersionNumber);
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => env.People.ReadImageAsync(person.Employee.Id, PersonnelImageKind.Avatar));
        env.Actor.CurrentUser = new User { Id = 99, Username = "other", CompanyScope = "C2", Role = UserRoleCatalog.Admin };
        await Assert.ThrowsAsync<ResourceNotFoundException>(() => env.People.ReadImageAsync(person.Employee.Id, PersonnelImageKind.IdentityFront));
    }

    [Fact]
    public async Task Images_ShouldRejectUnsupportedTruncatedOversizedOrCancelledContent_WithoutPartialWrites()
    {
        using var env = new PersonnelTestEnvironment();
        var person = await env.People.CreateAsync(env.Request());
        byte[] hugeDimensions = Png;
        BinaryPrimitives.WriteInt32BigEndian(hugeDimensions.AsSpan(16), 100000);
        foreach (byte[] invalid in new[] { Array.Empty<byte>(), "<svg/>"u8.ToArray(), Png[..24], hugeDimensions, new byte[PersonnelImageLimits.MaxBytes + 1] })
        {
            using var input = new MemoryStream(invalid);
            await Assert.ThrowsAsync<ServiceValidationException>(() => env.People.SaveImageAsync(person.Employee.Id, PersonnelImageKind.Avatar, person.VersionNumber, input, "image/png"));
        }
        using var mismatch = new MemoryStream(Png);
        await Assert.ThrowsAsync<ServiceValidationException>(() => env.People.SaveImageAsync(person.Employee.Id, PersonnelImageKind.Avatar, person.VersionNumber, mismatch, "image/jpeg"));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        using var content = new MemoryStream(Png);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.People.SaveImageAsync(person.Employee.Id, PersonnelImageKind.Avatar,
            person.VersionNumber, content, "image/png", cancelled.Token));
        await using var db = env.Database.CreateDbContext();
        Assert.Empty(await db.PersonnelImages.ToListAsync());
        Assert.Equal(person.VersionNumber, (await env.People.GetAsync(person.Employee.Id)).VersionNumber);
    }
}
