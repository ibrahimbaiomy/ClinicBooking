using System.Net;
using ClinicBooking.Application.Interfaces;
using ClinicBooking.Domain.Entities;
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
            specialty.NameEn = UniqueName();
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
        await Assert.ThrowsAsync<DbUpdateException>(() => AddSpecialtyAsync(nameAr, UniqueName()));
        await Assert.ThrowsAsync<DbUpdateException>(() => AddSpecialtyAsync(UniqueName(), nameEn));

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

    private static string UniqueName() => Guid.NewGuid().ToString("N");

    private async Task<long> AddSpecialtyAsync(string nameAr, string nameEn)
    {
        using var scope = _fixture.Factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAppDbContext>();
        var specialty = new Specialty { NameAr = nameAr, NameEn = nameEn };
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
