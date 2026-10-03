namespace ClinicBooking.Domain.Entities;

public class Specialty : SoftDeletableEntity
{
    public string NameAr { get; set; } = string.Empty;

    public string NameEn { get; set; } = string.Empty;
}
