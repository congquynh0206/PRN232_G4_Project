using System.Text;
using System.Text.RegularExpressions;
using G4.DemoData;
using G4.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;

var output = Path.GetFullPath("database/demo");
Directory.CreateDirectory(output);
var server = args.SkipWhile(x => x != "--server").Skip(1).FirstOrDefault() ?? "localhost";
var suffix = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
var resume = args.SkipWhile(x => x != "--export-existing").Skip(1).FirstOrDefault();
var buildName = resume ?? "G4_Demo_Build_" + suffix;
var connection = new SqlConnectionStringBuilder
{
    DataSource = server,
    InitialCatalog = buildName,
    IntegratedSecurity = true,
    TrustServerCertificate = true,
    ConnectTimeout = 10,
}.ConnectionString;
if (!Regex.IsMatch(buildName, "^G4_Demo_Build_[0-9_]+$"))
    throw new InvalidOperationException("Dedicated demo build database required.");
var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(connection).Options;
await using var db = new ApplicationDbContext(options);
var creator = db.Database.GetService<IRelationalDatabaseCreator>();
if (resume == null && await creator.ExistsAsync())
    throw new InvalidOperationException("Build database already exists; refusing to overwrite.");
if (resume == null)
{
    await creator.CreateAsync();
    await db.Database.ExecuteSqlRawAsync(
        """
        DECLARE @files nvarchar(max)=N'';
        SELECT @files += N'ALTER DATABASE '+QUOTENAME(DB_NAME())
            +N' MODIFY FILE (NAME=N'+QUOTENAME(name,'''')+N',FILEGROWTH=8MB);'
        FROM sys.database_files;
        EXEC sys.sp_executesql @files;
        """
    );
}
var baseline = Regex.Replace(
    await File.ReadAllTextAsync("database/baseline.sql"),
    @"(?im)^\s*(CREATE DATABASE CloneEbayDB;|USE CloneEbayDB;)\s*$",
    ""
);
if (resume == null)
    foreach (var batch in Regex.Split(baseline, @"(?im)^\s*GO\s*$"))
        if (!string.IsNullOrWhiteSpace(batch))
            await db.Database.ExecuteSqlRawAsync(batch);
if (resume == null)
    await db.Database.MigrateAsync();
Console.WriteLine("Schema ready: " + buildName);
var fixture = new DemoFixture(db);
if (resume == null)
    await fixture.CreateAsync();
else
    await fixture.LoadReadyAsync(Path.Combine(output, "HUONG_DAN_DEMO.md"));
var exporter = new DemoExporter(db, fixture);
var sql = await exporter.GenerateAsync(baseline);
await File.WriteAllTextAsync(Path.Combine(output, "G4_Demo.sql"), sql, new UTF8Encoding(true));
await File.WriteAllTextAsync(
    Path.Combine(output, "HUONG_DAN_DEMO.md"),
    fixture.Guide(),
    new UTF8Encoding(true)
);
Console.WriteLine($"Exported {fixture.Cases.Count} scenarios to {output}");
var qaName = "G4_Demo_QA_" + suffix;
var qaSql = sql.Replace(
    "DECLARE @DatabaseName sysname = N'G4_Demo';",
    $"DECLARE @DatabaseName sysname = N'{qaName}';"
);
var master = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
await using (var sqlConnection = new SqlConnection(master.ConnectionString))
{
    await sqlConnection.OpenAsync();
    await using var command = new SqlCommand(qaSql, sqlConnection) { CommandTimeout = 240 };
    await command.ExecuteNonQueryAsync();
    try
    {
        await command.ExecuteNonQueryAsync();
        throw new InvalidOperationException("SQL must refuse an existing database.");
    }
    catch (SqlException ex) when (ex.Number == 51000 && ex.Message.Contains("already exists"))
    {
        Console.WriteLine("Existing-database protection passed.");
    }
}
var qaConnection = new SqlConnectionStringBuilder(connection) { InitialCatalog = qaName };
await using var qaDb = new ApplicationDbContext(
    new DbContextOptionsBuilder<ApplicationDbContext>()
        .UseSqlServer(qaConnection.ConnectionString)
        .Options
);
await DemoValidation.RunAsync(qaDb, fixture, connection, qaConnection.ConnectionString);
await File.WriteAllTextAsync(
    Path.Combine(output, "validation.txt"),
    $"Generated UTC: {DateTime.UtcNow:O}\nBuild database: {buildName}\nImported SQL database: {qaName}\n"
        + $"Scenarios: {fixture.Cases.Count}\nSQL import, FK/check constraints, data comparison and service validation passed.\n"
        + "Primary database was not accessed or modified.\n",
    new UTF8Encoding(true)
);
Console.WriteLine("SQL import and validation passed: " + qaName);
