using ClinicBooking.Domain.Exceptions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ClinicBooking.Infrastructure.Persistence;

/// <summary>
/// Turns a unique-index violation into a <see cref="ConflictException"/> (409) with an error key,
/// but only for indexes that carry <see cref="ConflictKeyAnnotation"/>. Every other violation
/// (Identity, refresh tokens, ...) is not touched. Reused for D43's <c>slot_taken</c>.
/// </summary>
internal static class UniqueViolationTranslation
{
    public const string ConflictKeyAnnotation = "ClinicBooking:ConflictKey";

    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    public static bool TryGetConflictKey(IModel model, DbUpdateException exception, out string key)
    {
        key = string.Empty;
        if (exception.InnerException is not SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql)
        {
            return false;
        }

        // The index name is ASCII, so matching it inside the message does not depend on the
        // language of the SQL Server error text.
        foreach (var index in model.GetEntityTypes().SelectMany(type => type.GetIndexes()))
        {
            if (index.IsUnique
                && index.FindAnnotation(ConflictKeyAnnotation)?.Value is string conflictKey
                && index.GetDatabaseName() is { } name
                && sql.Message.Contains(name, StringComparison.Ordinal))
            {
                key = conflictKey;
                return true;
            }
        }

        return false;
    }
}
