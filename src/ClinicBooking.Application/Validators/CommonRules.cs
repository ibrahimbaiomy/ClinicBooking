using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Common;
using ClinicBooking.Domain.ValueObjects;
using FluentValidation;

namespace ClinicBooking.Application.Validators;

/// <summary>Rules shared by Specialties, Clinics and Doctors, extracted at the third copy (D61).</summary>
internal static class CommonRules
{
    /// <summary>Required, at most <paramref name="maxLength"/> characters (also after normalisation), and something searchable.</summary>
    public static IRuleBuilderOptions<T, string?> BeAName<T>(
        this IRuleBuilder<T, string?> builder,
        int maxLength,
        string requiredKey,
        string tooLongKey,
        string invalidKey) =>
        builder
            .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage(requiredKey)
            .Must(value => value!.Trim().Length <= maxLength).WithMessage(tooLongKey)
            .Must(value => SearchText.Normalize(value).Length > 0).WithMessage(invalidKey)
            .Must(value => SearchText.Normalize(value).Length <= maxLength).WithMessage(tooLongKey);

    /// <summary>Present and decodable; whether it is still current is the service's question.</summary>
    public static IRuleBuilderOptions<T, string?> BeARowVersion<T>(this IRuleBuilder<T, string?> builder) =>
        builder
            .Must(value => !string.IsNullOrWhiteSpace(value)).WithMessage(ConcurrencyErrors.RowVersionRequired)
            .Must(value => RowVersionCodec.TryDecode(value, out _)).WithMessage(ConcurrencyErrors.RowVersionInvalid);

    /// <summary>Paging, sorting and search of a <see cref="NamedListQuery"/> (D50).</summary>
    public static void AddNamedListRules<T>(this AbstractValidator<T> validator)
        where T : NamedListQuery
    {
        validator.RuleFor(q => q.Page).GreaterThanOrEqualTo(1).WithMessage("error.paging.page_invalid");

        validator.RuleFor(q => q.PageSize)
            .InclusiveBetween(1, NamedListQuery.MaxPageSize).WithMessage("error.paging.page_size_invalid");

        validator.RuleFor(q => q.SortBy)
            .Must(value => NameSortFields.All.Contains(value, StringComparer.OrdinalIgnoreCase))
            .WithMessage("error.sort.invalid");

        validator.RuleFor(q => q.SortDirection)
            .Must(value => value is not null
                && (value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                    || value.Equals("desc", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("error.sort.invalid");

        validator.RuleFor(q => q.Search)
            .MaximumLength(NamedListQuery.MaxSearchLength).WithMessage("error.search.too_long");
    }
}
