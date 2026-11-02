using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace CivicEyeGD.Services;

// PRAGMA важи само за връзката, на която е изпълнен, а EF отваря нова за
// всяка заявка. Изчакването се слага при самото отваряне, защото при
// споделен хостинг файлът кратко се държи от вече спряния процес.
public sealed class SqliteBusyTimeoutInterceptor : DbConnectionInterceptor
{
    // EF отваря връзката и синхронно, и асинхронно — в зависимост от
    // пътя на заявката. И двете места се прихващат, защото пропуснатата
    // половина би дала различно поведение в зависимост от кода на страницата.
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Apply(connection);
    }

    // PRAGMA е локална заповед към вече отворената връзка и не чака
    // нищо от мрежата, затова асинхронната версия просто прилага и
    // връща готов Task вместо фалшиво чакане.
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
        // using: командата се освобождава веднага след заповедта —
        // тя е еднократна настройка, не част от жизнения цикъл на връзката.
        using var command = connection.CreateCommand();
        // 5000 ms изчакване: при едновременни заявки SQLite заключва
        // файла при запис, и без изчакване втората веднага пада с
        // „database is locked“. Пет секунди превръщат рядкото съвпадение
        // в леко забавяне, а не в черна страница за гражданина.
        command.CommandText = "PRAGMA busy_timeout=5000;";
        command.ExecuteNonQuery();
    }
}
