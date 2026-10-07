using ClinicBooking.Application.DTOs;
using ClinicBooking.Domain.Entities;
using FluentValidation;

namespace ClinicBooking.Application.Validators;

public sealed class CreateSpecialtyRequestValidator : AbstractValidator<CreateSpecialtyRequest>
{
    public CreateSpecialtyRequestValidator()
    {
        RuleFor(r => r.NameAr).Cascade(CascadeMode.Stop).BeAName(
            Specialty.NameMaxLength, "error.specialty.name_ar_required", "error.specialty.name_ar_too_long", "error.specialty.name_ar_invalid");
        RuleFor(r => r.NameEn).Cascade(CascadeMode.Stop).BeAName(
            Specialty.NameMaxLength, "error.specialty.name_en_required", "error.specialty.name_en_too_long", "error.specialty.name_en_invalid");
    }
}

public sealed class UpdateSpecialtyRequestValidator : AbstractValidator<UpdateSpecialtyRequest>
{
    public UpdateSpecialtyRequestValidator()
    {
        RuleFor(r => r.NameAr).Cascade(CascadeMode.Stop).BeAName(
            Specialty.NameMaxLength, "error.specialty.name_ar_required", "error.specialty.name_ar_too_long", "error.specialty.name_ar_invalid");
        RuleFor(r => r.NameEn).Cascade(CascadeMode.Stop).BeAName(
            Specialty.NameMaxLength, "error.specialty.name_en_required", "error.specialty.name_en_too_long", "error.specialty.name_en_invalid");
        RuleFor(r => r.RowVersion).Cascade(CascadeMode.Stop).BeARowVersion();
    }
}

public sealed class ListSpecialtiesQueryValidator : AbstractValidator<ListSpecialtiesQuery>
{
    public ListSpecialtiesQueryValidator()
    {
        this.AddNamedListRules();
    }
}
