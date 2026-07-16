using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace FairShareMonApi.Database.Conversions;

/// <summary>
/// EF value converter that stamps every materialized <see cref="DateTime"/> with
/// <see cref="DateTimeKind.Utc"/> on the READ side, while WRITING the value through unchanged (all
/// columns already hold UTC instants). This fixes Pomelo materializing <c>datetime(6)</c> columns as
/// <see cref="DateTimeKind.Unspecified"/> - the root cause of the M5 audit representation-drift bug -
/// so downstream conversions (JSON/export presentation) and audit no-op detection are correct without
/// re-labelling values by hand.
///
/// <para>Applied by convention in <c>AppDbContext.ConfigureConventions</c> so all mapped
/// <see cref="DateTime"/> / <see cref="DateTime"/>? properties are covered. The write side is an
/// identity no-op (stores the same UTC value, same <c>datetime(6)</c> column), so the model snapshot /
/// column definitions are unchanged - no EF migration.</para>
/// </summary>
public sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
{
    public UtcDateTimeConverter()
        : base(
            value => value,                                          // write: persist the UTC value as-is
            value => DateTime.SpecifyKind(value, DateTimeKind.Utc))  // read: label the instant as UTC
    {
    }
}
