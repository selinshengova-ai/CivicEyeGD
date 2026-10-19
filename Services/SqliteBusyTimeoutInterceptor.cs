using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CivicEyeGD.Services;

// PRAGMA важи само за връзката, на която е изпълнен, а EF отваря нова за
// всяка заявка. Изчакването се слага при самото отваряне, защото при
// споделен хостинг файлът кратко се държи от вече спряния процес.
public sealed class SqliteBusyTimeoutInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Apply(connection);
        return Task.CompletedTask;
    }

    private static void Apply(DbConnection connection)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
    }
}
