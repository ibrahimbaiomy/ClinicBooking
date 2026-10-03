using System.Linq.Expressions;
using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.Entities;

namespace ClinicBooking.Application.Features.Specialties;

/// <summary>
/// The one hand-written mapping (D8). Queries project with <see cref="ToResponseExpression"/>, so no
/// entity is materialised; <see cref="ToResponse"/> is the same mapping for an entity already in memory.
/// </summary>
public static class SpecialtyMapping
{
    public static readonly Expression<Func<Specialty, SpecialtyResponse>> ToResponseExpression = s =>
        new SpecialtyResponse(s.Id, s.NameAr, s.NameEn, s.CreatedAt, s.UpdatedAt, RowVersionCodec.Encode(s.RowVersion));

    private static readonly Func<Specialty, SpecialtyResponse> Compiled = ToResponseExpression.Compile();

    public static SpecialtyResponse ToResponse(this Specialty specialty) => Compiled(specialty);
}
