using System.Data.Common;
using DiDecoration.Attributes;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FairShareMonApi.Database;

/// <summary>
/// Pins every pooled DB connection's session time zone to UTC (<c>SET time_zone = '+00:00'</c>) as
/// soon as it opens, so DB-generated timestamps (<c>UpdatedAt</c> =
/// <c>current_timestamp(6) ON UPDATE current_timestamp(6)</c>) are true UTC regardless of the server
/// OS/session zone. This is the UTC-correct analog of quick-ordering's <c>+07:00</c> interceptor -
/// FairShareMon keeps UTC storage, so it uses <c>+00:00</c> and never <c>+07:00</c>.
/// </summary>
[SingletonService]
public sealed class UtcSessionTimeZoneInterceptor : DbConnectionInterceptor
{
    private const string SetUtcSession = "SET time_zone = '+00:00'";

    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        using var command = connection.CreateCommand();
        command.CommandText = SetUtcSession;
        command.ExecuteNonQuery();
    }

    public override async Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = SetUtcSession;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
