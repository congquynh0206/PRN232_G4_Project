using System.Data;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using G4.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace G4.DemoData;

internal sealed class DemoExporter(ApplicationDbContext db, DemoFixture fixture)
{
    public async Task<string> GenerateAsync(string baseline)
    {
        var migration = db
            .Database.GetService<IMigrator>()
            .GenerateScript(options: MigrationsSqlGenerationOptions.NoTransactions);
        var setup = new StringBuilder(
            "SET NOCOUNT ON; SET XACT_ABORT ON;\nBEGIN TRY\nBEGIN TRANSACTION;\n"
        );
        foreach (var batch in Batches(baseline).Concat(Batches(migration)))
            setup.AppendLine("EXEC(" + Quote(batch) + ");");
        var connection = (SqlConnection)db.Database.GetDbConnection();
        if (connection.State != ConnectionState.Open)
            await connection.OpenAsync();
        var tables = await TablesAsync(connection);
        var seed = new StringBuilder(
            "DECLARE @DemoNow datetime2(7) = CONVERT(datetime2(7),GETUTCDATE());\n"
        );
        foreach (var table in tables)
            seed.AppendLine($"ALTER TABLE dbo.{Identifier(table)} NOCHECK CONSTRAINT ALL;");
        foreach (var table in tables.Where(x => x != "__EFMigrationsHistory"))
        {
            var columns = await ColumnsAsync(connection, table);
            var identity = columns.Any(x => x.Identity);
            var primary = columns.Where(x => x.Primary).ToArray();
            if (primary.Length == 0)
                throw new Exception("Export requires primary key: " + table);
            var names = string.Join(",", columns.Select(x => Identifier(x.Name)));
            await using var command = new SqlCommand(
                $"SELECT {names} FROM dbo.{Identifier(table)}",
                connection
            );
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<object[]>();
            while (await reader.ReadAsync())
            {
                var row = new object[columns.Length];
                reader.GetValues(row);
                rows.Add(row);
            }
            if (rows.Count == 0)
                continue;
            if (identity)
                seed.AppendLine($"SET IDENTITY_INSERT dbo.{Identifier(table)} ON;");
            foreach (var row in rows)
            {
                var values = columns.Select((c, i) => Literal(row[i], c.Name));
                var key = columns
                    .Select((c, i) => (c, i))
                    .Where(x => x.c.Primary)
                    .Select(x => Identifier(x.c.Name) + "=" + Literal(row[x.i], x.c.Name));
                seed.AppendLine(
                    $"INSERT INTO dbo.{Identifier(table)} ({names}) SELECT {string.Join(",", values)} "
                        + $"WHERE NOT EXISTS (SELECT 1 FROM dbo.{Identifier(table)} WHERE {string.Join(" AND ", key)});"
                );
            }
            if (identity)
                seed.AppendLine($"SET IDENTITY_INSERT dbo.{Identifier(table)} OFF;");
        }
        foreach (var table in tables)
            seed.AppendLine(
                $"ALTER TABLE dbo.{Identifier(table)} WITH CHECK CHECK CONSTRAINT ALL;"
            );
        seed.AppendLine(
            "UPDATE a SET SalesMonth=DATEFROMPARTS(YEAR(@DemoNow),MONTH(@DemoNow),1), "
                + "MonthlySalesAmount=COALESCE((SELECT SUM(CASE WHEN o.PricingSchemaVersion=1 THEN o.SellerGrossSnapshot ELSE p.amount END) "
                + "FROM dbo.Payment p JOIN dbo.OrderTable o ON o.id=p.orderId WHERE o.SellerId=a.SellerId AND p.status=N'Succeeded' "
                + "AND p.paidAt>=DATEFROMPARTS(YEAR(@DemoNow),MONTH(@DemoNow),1) AND p.paidAt<=DATEADD(SECOND,1,@DemoNow)),0) FROM dbo.SellerAccount a;"
        );
        seed.AppendLine("EXEC(N'CREATE SCHEMA demo');");
        seed.AppendLine(
            "CREATE TABLE demo.LoginAccount (Email nvarchar(100) PRIMARY KEY, Role nvarchar(20), Description nvarchar(255));"
        );
        foreach (var login in fixture.Logins)
            seed.AppendLine(
                $"INSERT demo.LoginAccount VALUES ({Quote(login.Email)},{Quote(login.Role)},{Quote(login.Description)});"
            );
        seed.AppendLine(
            "CREATE TABLE demo.Scenario (Code nvarchar(100) PRIMARY KEY, EntityType nvarchar(30), EntityId bigint, "
                + "SellerEmail nvarchar(100), FirstRole nvarchar(20), Steps nvarchar(2000), Expected nvarchar(2000), TimeSensitive bit);"
        );
        foreach (var c in fixture.Cases)
            seed.AppendLine(
                $"INSERT demo.Scenario VALUES ({Quote(c.Code)},{Quote(c.Type)},{c.Id},{Quote(c.SellerEmail)},"
                    + $"{Quote(c.Role)},{Quote(c.Steps)},{Quote(c.Expected)},{(c.Timed ? 1 : 0)});"
            );
        seed.AppendLine(
            "CREATE TABLE demo.Manifest (Id int PRIMARY KEY, ImportedAtUtc datetime2(7), FixtureVersion nvarchar(50));"
                + "INSERT demo.Manifest VALUES (1,@DemoNow,N'G4-demo-2026-10-31');"
        );
        seed.AppendLine(
            "IF EXISTS (SELECT 1 FROM dbo.SellerAccount WHERE ProcessingBalance<0 OR AvailableBalance<0 OR OnHoldBalance<0 OR NegativeBalance<0) "
                + "THROW 51000, 'Demo wallet invariant failed.', 1;"
        );
        setup.AppendLine("EXEC(" + Quote(seed.ToString()) + ");");
        setup.AppendLine(
            "COMMIT TRANSACTION;\nEND TRY\nBEGIN CATCH\nIF @@TRANCOUNT>0 ROLLBACK TRANSACTION;\nTHROW;\nEND CATCH;"
        );
        setup.AppendLine(
            "SELECT N'Demo ready. Password for all accounts: G4@123456. See HUONG_DAN_DEMO.md / Run-Demo.ps1.' AS Result;"
                + "SELECT * FROM demo.LoginAccount ORDER BY Email; SELECT * FROM demo.Scenario ORDER BY Code;"
        );
        var header = """
            /*
             G4 DEMO DATABASE - 31/10/2026
             Run the whole file in SSMS (normal mode) or sqlcmd -f 65001. No SQLCMD variables/includes.
             Creates a NEW dedicated database with baseline + all current EF migrations + demo data.
             NEVER drops, clears or overwrites an existing database. If the name exists, execution stops.
             Change ONLY @DatabaseName below for a second clean demo, e.g. G4_Demo_Lan2.
             Configure the application with Run-Demo.ps1 to match the demo identity key and timers.
             All users, identities, payments, labels, evidence and finance rows are synthetic demo fixtures.
             Historical data is a prepared scenario, not evidence of real provider transactions.
             Password for all demo accounts: G4@123456
            */
            SET NOCOUNT ON;
            SET XACT_ABORT ON;
            DECLARE @DatabaseName sysname = N'G4_Demo';
            IF @DatabaseName NOT LIKE N'G4[_]Demo%' OR @DatabaseName LIKE N'%[^a-zA-Z0-9_]%' COLLATE Latin1_General_100_BIN2
                THROW 51000, 'Only a dedicated G4_Demo... database name is allowed.', 1;
            IF DB_ID(@DatabaseName) IS NOT NULL
                THROW 51000, 'Database already exists. No data changed. Choose a NEW G4_Demo... name.', 1;
            DECLARE @Create nvarchar(max) = N'CREATE DATABASE ' + QUOTENAME(@DatabaseName);
            EXEC sys.sp_executesql @Create;
            -- Restrict growth to 8 MB increments on this NEW demo database only.
            DECLARE @Files nvarchar(max) = N'';
            SELECT @Files += N'ALTER DATABASE ' + QUOTENAME(@DatabaseName)
                + N' MODIFY FILE (NAME=N' + QUOTENAME(name,'''') + N',FILEGROWTH=8MB);'
            FROM sys.master_files WHERE database_id=DB_ID(@DatabaseName);
            EXEC sys.sp_executesql @Files;
            DECLARE @Setup nvarchar(max) =
            """;
        // A Unicode literal above 4000 characters is nvarchar(max). Keep it as one literal:
        // a long concatenation expression can create quadratic LOB work in tempdb.
        var wrapped = "USE __DEMO_DATABASE__;\n" + setup;
        return header
            + "\n"
            + Quote(wrapped)
            + ";\nSET @Setup=REPLACE(@Setup,N'__DEMO_DATABASE__',QUOTENAME(@DatabaseName));\n"
            + "EXEC sys.sp_executesql @Setup;\n";
    }

    public string Literal(object? value, string column = "")
    {
        if (value is null || value is DBNull)
            return "NULL";
        if (value is string text)
            return Quote(text);
        if (value is bool boolean)
            return boolean ? "1" : "0";
        if (value is byte[] bytes)
            return "0x" + Convert.ToHexString(bytes);
        if (value is DateTimeOffset offset)
            return "TODATETIMEOFFSET(" + Date(offset.UtcDateTime, column) + ",'+00:00')";
        if (value is DateTime date)
            return Date(date, column);
        if (value is Guid guid)
            return Quote(guid.ToString());
        return Convert.ToString(value, CultureInfo.InvariantCulture)!;
    }

    private string Date(DateTime value, string column)
    {
        if (column == "SalesMonth")
            return "DATEFROMPARTS(YEAR(@DemoNow),MONTH(@DemoNow),1)";
        if (value.Year < 2001 || value.Year >= 2090)
            return $"CONVERT(datetime2(7),'{value:yyyy-MM-ddTHH:mm:ss.fffffff}',126)";
        var delta = (long)Math.Round((value - fixture.Anchor).TotalMilliseconds);
        if (
            Math.Abs(delta) < 1000
            || delta > 0
                && column
                    is "CreatedAt"
                        or "UpdatedAt"
                        or "CapturedAt"
                        or "SentAt"
                        or "LastAttemptAt"
                        or "CompletedAt"
        )
            delta = 0;
        return $"DATEADD(MILLISECOND,{delta % 1000},DATEADD(SECOND,{delta / 1000},@DemoNow))";
    }

    public static string Quote(string value) => "N'" + value.Replace("'", "''") + "'";

    public static string Identifier(string value) => "[" + value.Replace("]", "]]") + "]";

    private static IEnumerable<string> Batches(string sql) =>
        Regex.Split(sql, @"(?im)^\s*GO\s*$").Where(x => !string.IsNullOrWhiteSpace(x));

    public static async Task<string[]> TablesAsync(SqlConnection connection)
    {
        await using var command = new SqlCommand(
            "SELECT name FROM sys.tables WHERE schema_id=SCHEMA_ID('dbo') ORDER BY name",
            connection
        );
        await using var reader = await command.ExecuteReaderAsync();
        var names = new List<string>();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));
        return names.ToArray();
    }

    private static async Task<Column[]> ColumnsAsync(SqlConnection connection, string table)
    {
        const string sql =
            "SELECT c.name,c.is_identity,CONVERT(bit,CASE WHEN EXISTS (SELECT 1 FROM sys.indexes i "
            + "JOIN sys.index_columns ic ON i.object_id=ic.object_id AND i.index_id=ic.index_id "
            + "WHERE i.object_id=c.object_id AND i.is_primary_key=1 AND ic.column_id=c.column_id) THEN 1 ELSE 0 END) "
            + "FROM sys.columns c WHERE c.object_id=OBJECT_ID(@table) AND c.is_computed=0 AND c.system_type_id<>189 ORDER BY c.column_id";
        await using var command = new SqlCommand(sql, connection);
        command.Parameters.AddWithValue("@table", "dbo." + table);
        await using var reader = await command.ExecuteReaderAsync();
        var columns = new List<Column>();
        while (await reader.ReadAsync())
            columns.Add(new(reader.GetString(0), reader.GetBoolean(1), reader.GetBoolean(2)));
        return columns.ToArray();
    }

    private sealed record Column(string Name, bool Identity, bool Primary);
}
