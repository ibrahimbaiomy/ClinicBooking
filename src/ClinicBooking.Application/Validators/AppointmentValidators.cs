using ClinicBooking.Application.DTOs;
using ClinicBooking.Application.Features.Appointments;
using ClinicBooking.Application.Features.Doctors;
using FluentValidation;

namespace ClinicBooking.Application.Validators;

public sealed class BookAppointmentRequestValidator : AbstractValidator<BookAppointmentRequest>
{
    public BookAppointmentRequestValidator()
    {
        RuleFor(r => r.DoctorId).Must(id => id is > 0).WithMessage(AppointmentErrors.DoctorRequired);
        RuleFor(r => r.PatientId).Must(id => id is > 0).WithMessage(AppointmentErrors.PatientRequired);

        // Whether the start is in the future and on the grid are rules of the clock and the schedule (422).
        RuleFor(r => r.StartUtc).Must(start => start is not null).WithMessage(AppointmentErrors.StartRequired);
    }
}

public sealed class RescheduleAppointmentRequestValidator : AbstractValidator<RescheduleAppointmentRequest>
{
    public RescheduleAppointmentRequestValidator()
    {
        RuleFor(r => r.StartUtc).Must(start => start is not null).WithMessage(AppointmentErrors.StartRequired);
        RuleFor(r => r.RowVersion).Cascade(CascadeMode.Stop).BeARowVersion();
    }
}

public sealed class ChangeAppointmentStatusRequestValidator : AbstractValidator<ChangeAppointmentStatusRequest>
{
    public ChangeAppointmentStatusRequestValidator()
    {
        RuleFor(r => r.RowVersion).Cascade(CascadeMode.Stop).BeARowVersion();
    }
}

public sealed class DayAppointmentsQueryValidator : AbstractValidator<DayAppointmentsQuery>
{
    public DayAppointmentsQueryValidator()
    {
        RuleFor(q => q.Date).Must(date => date is not null).WithMessage(AppointmentErrors.DateRequired);
    }
}

public sealed class AvailableSlotsQueryValidator : AbstractValidator<AvailableSlotsQuery>
{
    public AvailableSlotsQueryValidator()
    {
        RuleFor(q => q.Date).Must(date => date is not null).WithMessage(AppointmentErrors.DateRequired);
    }
}

public sealed class AddLeaveDaysRequestValidator : AbstractValidator<AddLeaveDaysRequest>
{
    /// <summary>At most this many days per request, both ends included (D66).</summary>
    public const int MaxDays = 60;

    public AddLeaveDaysRequestValidator() => this.AddLeaveRangeRules(r => r.From, r => r.To, MaxDays);
}

public sealed class LeaveDaysQueryValidator : AbstractValidator<LeaveDaysQuery>
{
    /// <summary>The longest range one read returns (D66).</summary>
    public const int MaxDays = 366;

    public LeaveDaysQueryValidator() => this.AddLeaveRangeRules(q => q.From, q => q.To, MaxDays);
}

/// <summary>Only the confirmation flag; nothing to check, but every request type has a validator (D50).</summary>
public sealed class DeactivateClinicRequestValidator : AbstractValidator<DeactivateClinicRequest>
{
}

internal static class LeaveRules
{
    public static void AddLeaveRangeRules<T>(
        this AbstractValidator<T> validator,
        System.Linq.Expressions.Expression<Func<T, DateOnly?>> from,
        System.Linq.Expressions.Expression<Func<T, DateOnly?>> to,
        int maxDays)
    {
        var readFrom = from.Compile();
        validator.RuleFor(from).Must(date => date is not null).WithMessage(DoctorErrors.LeaveFromRequired);
        validator.RuleFor(to).Cascade(CascadeMode.Stop)
            .Must(date => date is not null).WithMessage(DoctorErrors.LeaveToRequired)
            .Must((request, date) => readFrom(request) is not { } start || date!.Value >= start)
            .WithMessage(DoctorErrors.LeaveRangeInvalid)
            .Must((request, date) => readFrom(request) is not { } start || date!.Value.DayNumber - start.DayNumber + 1 <= maxDays)
            .WithMessage(DoctorErrors.LeaveRangeTooLong);
    }
}
