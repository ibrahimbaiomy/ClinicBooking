using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.Entities;
using ClinicBooking.Domain.ValueObjects;
using FluentValidation;

namespace ClinicBooking.Application.Validators;

internal static class SpecialtyRules
{
    /// <summary>Required, at most 100 characters (also after normalisation), and something searchable.</summary>
    public static IRuleBuilderOptions<T, string?> BeASpecialtyName<T>(
        this IRuleBuilder<T, string?> builder,
        string requiredKey,
        string tooLongKey,
        string invalidKey) =>
        builder
            .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage(requiredKey)
            .Must(value => value!.Trim().Length <= Specialty.NameMaxLength).WithMessage(tooLongKey)
            .Must(value => SearchText.Normalize(value).Length > 0).WithMessage(invalidKey)
            .Must(value => SearchText.Normalize(value).Length <= Specialty.NameMaxLength).WithMessage(tooLongKey);
}

public sealed class CreateSpecialtyRequestValidator : AbstractValidator<CreateSpecialtyRequest>
{
    public CreateSpecialtyRequestValidator()
    {
        RuleFor(r => r.NameAr).Cascade(CascadeMode.Stop).BeASpecialtyName(
            "error.specialty.name_ar_required", "error.specialty.name_ar_too_long", "error.specialty.name_ar_invalid");
        RuleFor(r => r.NameEn).Cascade(CascadeMode.Stop).BeASpecialtyName(
            "error.specialty.name_en_required", "error.specialty.name_en_too_long", "error.specialty.name_en_invalid");
    }
}

public sealed class UpdateSpecialtyRequestValidator : AbstractValidator<UpdateSpecialtyRequest>
{
    public UpdateSpecialtyRequestValidator()
    {
        RuleFor(r => r.NameAr).Cascade(CascadeMode.Stop).BeASpecialtyName(
            "error.specialty.name_ar_required", "error.specialty.name_ar_too_long", "error.specialty.name_ar_invalid");
        RuleFor(r => r.NameEn).Cascade(CascadeMode.Stop).BeASpecialtyName(
            "error.specialty.name_en_required", "error.specialty.name_en_too_long", "error.specialty.name_en_invalid");

        RuleFor(r => r.RowVersion).Cascade(CascadeMode.Stop)
            .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage(ConcurrencyErrors.RowVersionRequired)
            .Must(value => RowVersionCodec.TryDecode(value, out _)).WithMessage(ConcurrencyErrors.RowVersionInvalid);
    }
}

public sealed class ListSpecialtiesQueryValidator : AbstractValidator<ListSpecialtiesQuery>
{
    public ListSpecialtiesQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1).WithMessage("error.paging.page_invalid");

        RuleFor(q => q.PageSize)
            .InclusiveBetween(1, ListSpecialtiesQuery.MaxPageSize).WithMessage("error.paging.page_size_invalid");

        RuleFor(q => q.SortBy)
            .Must(value => SpecialtySortFields.All.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("error.sort.invalid");

        RuleFor(q => q.SortDirection)
            .Must(value => value is not null
                && (value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("desc", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("error.sort.invalid");

        RuleFor(q => q.Search)
            .MaximumLength(ListSpecialtiesQuery.MaxSearchLength).WithMessage("error.search.too_long");
    }
}
