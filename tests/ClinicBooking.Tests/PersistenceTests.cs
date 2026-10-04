using System.Net;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.Exceptions;
using ClinicBooking.Infrastructure.Identity;
using ClinicBooking.Infrastructure.Persistence;
using ClinicBooking.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace ClinicBooking.Tests;

[Collection(SqlServerCollection.Name)]
public class PersistenceTests : IClassFixture<ApiDatabaseFixture>
{
    private readonly ApiDatabaseFixture _fixture;

    public PersistenceTests(ApiDatabaseFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task Audit_fields_are_set_on_create_and_update()
    {
        var created = new DateTimeOffset(2026, 3, 1, 10, 0, 0, TimeSpan.Zero);
        var updated = new DateTimeOffset(2026, 3, 2, 11, 30, 0, TimeSpan.Zero);

        _fixture.Clock.Now = created;
        _fixture.User.Id = 42;
        var id = await AddSpecialtyAsync(UniqueName(), UniqueName());

        var afterCreate = await GetIncludingDeletedAsync(id);
        Assert.Equal(created, afterCreate.CreatedAt);
        Assert.Equal(42, afterCreate.CreatedBy);
        Assert.Null(afterCreate.UpdatedAt);
        Assert.Null(afterCreate.UpdatedBy);

        _fixture.Clock.Now = updated;
        _fixture.User.Id = 7;
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            var specialty = await db.Specialties.SingleAsync(s => s.Id == id);
            specialty.SetNames(specialty.NameAr, UniqueName());
            await db.SaveChangesAsync();
        }

        var afterUpdate = await GetIncludingDeletedAsync(id);
        Assert.Equal(updated, afterUpdate.UpdatedAt);
        Assert.Equal(7, afterUpdate.UpdatedBy);
        Assert.Equal(created, afterUpdate.CreatedAt);
        Assert.Equal(42, afterUpdate.CreatedBy);
    }

    [Fact]
    public async Task Remove_is_a_soft_delete_hidden_by_the_global_filter()
    {
        var deleted = new DateTimeOffset(2026, 4, 1, 9, 0, 0, TimeSpan.Zero);
        _fixture.User.Id = 5;
        var id = await AddSpecialtyAsync(UniqueName(), UniqueName());

        _fixture.Clock.Now = deleted;
        _fixture.User.Id = 9;
        await RemoveSpecialtyAsync(id);

        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
            Assert.False(await db.Specialties.AnyAsync(s => s.Id == id));
        }

        var row = await GetIncludingDeletedAsync(id);
        Assert.True(row.IsDeleted);
        Assert.Equal(deleted, row.DeletedAt);
        Assert.Equal(9, row.DeletedBy);
        Assert.Equal(deleted, row.UpdatedAt);
    }

    [Fact]
    public async Task Deleted_names_can_be_recreated_but_live_duplicates_are_rejected()
    {
        var nameAr = UniqueName();
        var nameEn = UniqueName();

        var first = await AddSpecialtyAsync(nameAr, nameEn);
        var arTaken = await Assert.ThrowsAsync<ConflictException>(() => AddSpecialtyAsync(nameAr, UniqueName()));
        Assert.Equal("error.specialty.name_ar_taken", arTaken.ErrorKey);
        var enTaken = await Assert.ThrowsAsync<ConflictException>(() => AddSpecialtyAsync(UniqueName(), nameEn));
        Assert.Equal("error.specialty.name_en_taken", enTaken.ErrorKey);

        await RemoveSpecialtyAsync(first);

        var second = await AddSpecialtyAsync(nameAr, nameEn);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Arabic_names_round_trip_as_unicode()
    {
        var nameAr = "أمراض القلب والأوعية الدموية";
        var id = await AddSpecialtyAsync(nameAr + Guid.NewGuid().ToString("N"), UniqueName());

        var row = await GetIncludingDeletedAsync(id);

        Assert.StartsWith(nameAr, row.NameAr);
    }

    [Fact]
    public async Task Health_ready_returns_200_against_the_real_database()
    {
        using var client = _fixture.Factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_duplicate_user_name_at_the_index_becomes_a_user_name_taken_conflict()
    {
        // The unique user name index carries the conflict key of user management (D57).
        var normalized = $"DUP_{Guid.NewGuid():N}";
        using (var scope = _fixture.Factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new ApplicationUser { UserName = normalized, NormalizedUserName = normalized, SecurityStamp = "a" });
            await db.SaveChangesAsync();
        }

        using var second = _fixture.Factory.Services.CreateScope();
        var context = second.ServiceProvider.GetRequiredService<AppDbContext>();
        context.Users.Add(new ApplicationUser { UserName = normalized + "x", NormalizedUserName = normalized, SecurityStamp = "b" });

        var failure = await Assert.ThrowsAsync<ConflictException>(() => context.SaveChangesAsync());
        Assert.Equal("error.user.user_name_taken", failure.ErrorKey);
    }

    [Fact]
    public async Task A_duplicate_refresh_token_hash_is_rethrown_unchanged()
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.Identity.UserManager<ApplicationUser>>();
        var user = new ApplicationUser { UserName = $"rt_{Guid.NewGuid():N}" };
        Assert.True((await users.CreateAsync(user, "Correct-Horse-9-Battery")).Succeeded);

        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var hash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        var now = DateTimeOffset.UtcNow;
        RefreshToken Token() => new()
        {
            UserId = user.Id,
            TokenHash = hash,
            FamilyId = Guid.NewGuid(),
            CreatedAt = now,
            FamilyCreatedAt = now,
            ExpiresAt = now.AddDays(1)
        };

        db.RefreshTokens.Add(Token());
        await db.SaveChangesAsync();
        db.RefreshTokens.Add(Token());

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.IsNotType<ConflictException>(failure);
    }

    private static string UniqueName() => Guid.NewGuid().ToString("N");

    private async Task<long> AddSpecialtyAsync(string nameAr, string nameEn)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var specialty = Specialty.Create(nameAr, nameEn);
        db.Specialties.Add(specialty);
        await db.SaveChangesAsync();
        return specialty.Id;
    }

    private async Task RemoveSpecialtyAsync(long id)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var specialty = await db.Specialties.SingleAsync(s => s.Id == id);
        db.Specialties.Remove(specialty);
        await db.SaveChangesAsync();
    }

    private async Task<Specialty> GetIncludingDeletedAsync(long id)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        return await db.Specialties.IgnoreQueryFilters().AsNoTracking().SingleAsync(s => s.Id == id);
    }
}
