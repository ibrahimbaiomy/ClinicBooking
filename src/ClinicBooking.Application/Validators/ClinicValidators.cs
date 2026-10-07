using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Clinics;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.ValueObjects;
using FluentValidation;

namespace ClinicBooking.Application.Validators;

internal static class ClinicRules
{
    /// <summary>Optional free text, at most 300 characters after trimming.</summary>
    public static IRuleBuilderOptions<T, string?> BeAnOptionalAddress<T>(this IRuleBuilder<T, string?> builder) =>
        builder.Must(value => value is null || value.Trim().Length <= Clinic.AddressMaxLength)
            .WithMessage(ClinicErrors.AddressTooLong);

    /// <summary>Optional; blank is absent, anything else must normalise to E.164 (D55).</summary>
    public static IRuleBuilderOptions<T, string?> BeAnOptionalPhone<T>(this IRuleBuilder<T, string?> builder) =>
        builder.Must(value => PhoneNumber.TryNormalize(value, out _)).WithMessage(ClinicErrors.PhoneInvalid);
}

public sealed class CreateClinicRequestValidator : AbstractValidator<CreateClinicRequest>
{
    public CreateClinicRequestValidator()
    {
        RuleFor(r => r.NameAr).Cascade(CascadeMode.Stop).BeAName(
            Clinic.NameMaxLength, "error.clinic.name_ar_required", "error.clinic.name_ar_too_long", "error.clinic.name_ar_invalid");
        RuleFor(r => r.NameEn).Cascade(CascadeMode.Stop).BeAName(
            Clinic.NameMaxLength, "error.clinic.name_en_required", "error.clinic.name_en_too_long", "error.clinic.name_en_invalid");
        RuleFor(r => r.Address).BeAnOptionalAddress();
        RuleFor(r => r.Phone).BeAnOptionalPhone();
    }
}

public sealed class UpdateClinicRequestValidator : AbstractValidator<UpdateClinicRequest>
{
    public UpdateClinicRequestValidator()
    {
        RuleFor(r => r.NameAr).Cascade(CascadeMode.Stop).BeAName(
            Clinic.NameMaxLength, "error.clinic.name_ar_required", "error.clinic.name_ar_too_long", "error.clinic.name_ar_invalid");
        RuleFor(r => r.NameEn).Cascade(CascadeMode.Stop).BeAName(
            Clinic.NameMaxLength, "error.clinic.name_en_required", "error.clinic.name_en_too_long", "error.clinic.name_en_invalid");
        RuleFor(r => r.Address).BeAnOptionalAddress();
        RuleFor(r => r.Phone).BeAnOptionalPhone();
        RuleFor(r => r.RowVersion).Cascade(CascadeMode.Stop).BeARowVersion();
    }
}

public sealed class ListClinicsQueryValidator : AbstractValidator<ListClinicsQuery>
{
    public ListClinicsQueryValidator()
    {
        this.AddNamedListRules();
    }
}
