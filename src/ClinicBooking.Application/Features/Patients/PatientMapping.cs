using System.Linq.Expressions;
using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.Entities;

namespace ClinicBooking.Application.Features.Patients;

/// <summary>The one hand-written mapping (D8).</summary>
public static class PatientMapping
{
    public static readonly Expression<Func<Patient, PatientResponse>> ToResponseExpression = p =>
        new PatientResponse(p.Id, p.Name, p.Phone, p.CreatedAt, p.UpdatedAt, RowVersionCodec.Encode(p.RowVersion));

    private static readonly Func<Patient, PatientResponse> Compiled = ToResponseExpression.Compile();

    public static PatientResponse ToResponse(this Patient patient) => Compiled(patient);
}
