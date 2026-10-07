using System.Linq.Expressions;
using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Doctors;
using ClinicBooking.Domain.Entities;
using FluentValidation;

namespace ClinicBooking.Application.Validators;

internal static class DoctorRules
{
    public const int MaxSpecialties = 10;
    public const int MaxClinics = 20;

    public static void AddNameRules<T>(this AbstractValidator<T> validator, Expression<Func<T, string?>> nameAr, Expression<Func<T, string?>> nameEn)
    {
        validator.RuleFor(nameAr).Cascade(CascadeMode.Stop).BeAName(
            Doctor.NameMaxLength, "error.doctor.name_ar_required", "error.doctor.name_ar_too_long", "error.doctor.name_ar_invalid");
        validator.RuleFor(nameEn).Cascade(CascadeMode.Stop).BeAName(
            Doctor.NameMaxLength, "error.doctor.name_en_required", "error.doctor.name_en_too_long", "error.doctor.name_en_invalid");
    }

    /// <summary>At least one id and at most <paramref name="max"/> distinct ones (duplicates are ignored).</summary>
    public static IRuleBuilderOptions<T, IReadOnlyList<long>?> BeAnIdList<T>(
        this IRuleBuilder<T, IReadOnlyList<long>?> builder,
        int max,
        string requiredKey,
        string tooManyKey) =>
        builder
            .Must(ids => ids is { Count: > 0 }).WithMessage(requiredKey)
            .Must(ids => ids!.Distinct().Count() <= max).WithMessage(tooManyKey);

    public static IRuleBuilderOptions<T, int?> BeASlotDuration<T>(this IRuleBuilder<T, int?> builder) =>
        builder
            .Must(minutes => minutes is not null).WithMessage(DoctorErrors.SlotMinutesRequired)
            .Must(minutes => DoctorSlotDuration.IsValidMinutes(minutes!.Value)).WithMessage(DoctorErrors.SlotMinutesInvalid);
}

public sealed class CreateDoctorRequestValidator : AbstractValidator<CreateDoctorRequest>
{
    public CreateDoctorRequestValidator()
    {
        this.AddNameRules(r => r.NameAr, r => r.NameEn);
        RuleFor(r => r.SpecialtyIds).Cascade(CascadeMode.Stop)
            .BeAnIdList(DoctorRules.MaxSpecialties, DoctorErrors.SpecialtiesRequired, DoctorErrors.SpecialtiesTooMany);
        RuleFor(r => r.ClinicIds).Cascade(CascadeMode.Stop)
            .BeAnIdList(DoctorRules.MaxClinics, DoctorErrors.ClinicsRequired, DoctorErrors.ClinicsTooMany);
        RuleFor(r => r.SlotMinutes).Cascade(CascadeMode.Stop).BeASlotDuration();
    }
}

public sealed class UpdateDoctorRequestValidator : AbstractValidator<UpdateDoctorRequest>
{
    public UpdateDoctorRequestValidator()
    {
        this.AddNameRules(r => r.NameAr, r => r.NameEn);
        RuleFor(r => r.SpecialtyIds).Cascade(CascadeMode.Stop)
            .BeAnIdList(DoctorRules.MaxSpecialties, DoctorErrors.SpecialtiesRequired, DoctorErrors.SpecialtiesTooMany);
        RuleFor(r => r.RowVersion).Cascade(CascadeMode.Stop).BeARowVersion();
    }
}

public sealed class ListDoctorsQueryValidator : AbstractValidator<ListDoctorsQuery>
{
    public ListDoctorsQueryValidator()
    {
        this.AddNamedListRules();

        // The filter means "active in that clinic"; on its own it has no meaning and is never ignored (D61).
        RuleFor(q => q.IsActive)
            .Must((query, isActive) => isActive is null || query.ClinicId is not null)
            .WithMessage(DoctorErrors.IsActiveRequiresClinic);
    }
}
