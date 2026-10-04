using System.Linq.Expressions;
using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.Entities;

namespace ClinicBooking.Application.Features.Clinics;

/// <summary>
/// The one hand-written mapping (D8). Queries project with <see cref="ToResponseExpression"/>, so no
/// entity is materialised; <see cref="ToResponse"/> is the same mapping for an entity already in memory.
/// </summary>
public static class ClinicMapping
{
    public static readonly Expression<Func<Clinic, ClinicResponse>> ToResponseExpression = c =>
        new ClinicResponse(
            c.Id, c.NameAr, c.NameEn, c.Address, c.Phone, c.CreatedAt, c.UpdatedAt, RowVersionCodec.Encode(c.RowVersion));

    private static readonly Func<Clinic, ClinicResponse> Compiled = ToResponseExpression.Compile();

    public static ClinicResponse ToResponse(this Clinic clinic) => Compiled(clinic);
}
