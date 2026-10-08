using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Patients;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.ValueObjects;
using FluentValidation;

namespace ClinicBooking.Application.Validators;

internal static class PatientRules
{
    /// <summary>Required, at most 100 characters (also after normalisation), and something searchable.</summary>
    public static IRuleBuilderOptions<T, string?> BeAPatientName<T>(this IRuleBuilder<T, string?> builder) =>
        builder.BeAName(Patient.NameMaxLength, PatientErrors.NameRequired, PatientErrors.NameTooLong, PatientErrors.NameInvalid);

    /// <summary>Required, and it must normalise to E.164 (D55, D63).</summary>
    public static IRuleBuilderOptions<T, string?> BeAPatientPhone<T>(this IRuleBuilder<T, string?> builder) =>
        builder
            .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage(PatientErrors.PhoneRequired)
            .Must(value => PhoneNumber.TryNormalize(value, out _)).WithMessage(PatientErrors.PhoneInvalid);
}

public sealed class CreatePatientRequestValidator : AbstractValidator<CreatePatientRequest>
{
    public CreatePatientRequestValidator()
    {
        RuleFor(r => r.Name).Cascade(CascadeMode.Stop).BeAPatientName();
        RuleFor(r => r.Phone).Cascade(CascadeMode.Stop).BeAPatientPhone();
    }
}

public sealed class UpdatePatientRequestValidator : AbstractValidator<UpdatePatientRequest>
{
    public UpdatePatientRequestValidator()
    {
        RuleFor(r => r.Name).Cascade(CascadeMode.Stop).BeAPatientName();
        RuleFor(r => r.Phone).Cascade(CascadeMode.Stop).BeAPatientPhone();
        RuleFor(r => r.RowVersion).Cascade(CascadeMode.Stop).BeARowVersion();
    }
}

public sealed class ListPatientsQueryValidator : AbstractValidator<ListPatientsQuery>
{
    public ListPatientsQueryValidator()
    {
        this.AddListRules(PatientSortFields.All);
    }
}
