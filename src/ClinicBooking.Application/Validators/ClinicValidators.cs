using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Clinics;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.ValueObjects;
using FluentValidation;

namespace ClinicBooking.Application.Validators;

internal static class ClinicRules
{
    /// <summary>Required, at most 100 characters (also after normalisation), and something searchable.</summary>
    public static IRuleBuilderOptions<T, string?> BeAClinicName<T>(
        this IRuleBuilder<T, string?> builder,
        string requiredKey,
        string tooLongKey,
        string invalidKey) =>
        builder
            .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage(requiredKey)
            .Must(value => value!.Trim().Length <= Clinic.NameMaxLength).WithMessage(tooLongKey)
            .Must(value => SearchText.Normalize(value).Length > 0).WithMessage(invalidKey)
            .Must(value => SearchText.Normalize(value).Length <= Clinic.NameMaxLength).WithMessage(tooLongKey);

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
        RuleFor(r => r.NameAr).Cascade(CascadeMode.Stop).BeAClinicName(
            "error.clinic.name_ar_required", "error.clinic.name_ar_too_long", "error.clinic.name_ar_invalid");
        RuleFor(r => r.NameEn).Cascade(CascadeMode.Stop).BeAClinicName(
            "error.clinic.name_en_required", "error.clinic.name_en_too_long", "error.clinic.name_en_invalid");
        RuleFor(r => r.Address).BeAnOptionalAddress();
        RuleFor(r => r.Phone).BeAnOptionalPhone();
    }
}

public sealed class UpdateClinicRequestValidator : AbstractValidator<UpdateClinicRequest>
{
    public UpdateClinicRequestValidator()
    {
        RuleFor(r => r.NameAr).Cascade(CascadeMode.Stop).BeAClinicName(
            "error.clinic.name_ar_required", "error.clinic.name_ar_too_long", "error.clinic.name_ar_invalid");
        RuleFor(r => r.NameEn).Cascade(CascadeMode.Stop).BeAClinicName(
            "error.clinic.name_en_required", "error.clinic.name_en_too_long", "error.clinic.name_en_invalid");
        RuleFor(r => r.Address).BeAnOptionalAddress();
        RuleFor(r => r.Phone).BeAnOptionalPhone();

        RuleFor(r => r.RowVersion).Cascade(CascadeMode.Stop)
            .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage(ConcurrencyErrors.RowVersionRequired)
            .Must(value => RowVersionCodec.TryDecode(value, out _)).WithMessage(ConcurrencyErrors.RowVersionInvalid);
    }
}

public sealed class ListClinicsQueryValidator : AbstractValidator<ListClinicsQuery>
{
    public ListClinicsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).WithMessage("error.paging.page_invalid");

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, ListClinicsQuery.MaxPageSize).WithMessage("error.paging.page_size_invalid");

        RuleFor(q => q.SortBy)
            .Must(value => ClinicSortFields.All.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("error.sort.invalid");

        RuleFor(q => q.SortDirection)
            .Must(value => value is not null
                && (value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("desc", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("error.sort.invalid");

        RuleFor(q => q.Search)
            .MaximumLength(ListClinicsQuery.MaxSearchLength).WithMessage("error.search.too_long");
    }
}
