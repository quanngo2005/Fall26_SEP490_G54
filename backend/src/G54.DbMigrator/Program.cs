using G54.BLL.Services;
using G54.DAL;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(connectionString))
{
    var connectionStringBuilder = new NpgsqlConnectionStringBuilder
    {
        Host = "localhost",
        Port = 5432,
        Database = Environment.GetEnvironmentVariable("POSTGRES_DB") ?? "bpms_db",
        Username = Environment.GetEnvironmentVariable("POSTGRES_USER") ?? "postgres",
        Password = Environment.GetEnvironmentVariable("POSTGRES_PASSWORD") ?? "postgres",
    };
    connectionString = connectionStringBuilder.ConnectionString;
}

var bootstrapAdminEnabled = string.Equals(
    Environment.GetEnvironmentVariable("BootstrapAdmin__Enabled"),
    "true",
    StringComparison.OrdinalIgnoreCase);
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
if (bootstrapAdminEnabled && !string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
{
    throw new InvalidOperationException("Bootstrap admin seeding is allowed only in the Development environment.");
}

var bootstrapAdminEmail = Environment.GetEnvironmentVariable("BootstrapAdmin__Email");
var bootstrapAdminPassword = Environment.GetEnvironmentVariable("BootstrapAdmin__Password");
if (bootstrapAdminEnabled
    && (string.IsNullOrWhiteSpace(bootstrapAdminEmail) || string.IsNullOrEmpty(bootstrapAdminPassword)))
{
    throw new InvalidOperationException(
        "BootstrapAdmin__Email and BootstrapAdmin__Password are required when BootstrapAdmin__Enabled is true.");
}

var services = new ServiceCollection();
services.AddDataAccess(connectionString);
services.AddSingleton<IPasswordEncoder, PasswordEncoder>();
await using var serviceProvider = services.BuildServiceProvider();
await using var scope = serviceProvider.CreateAsyncScope();
var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

Console.WriteLine("Applying database migrations...");
await dbContext.Database.MigrateAsync();
var passwordEncoder = scope.ServiceProvider.GetRequiredService<IPasswordEncoder>();
var seeder = scope.ServiceProvider.GetRequiredService<DatabaseSeeder>();
Console.WriteLine("Seeding master data (departments, positions, employees, accounts, KPIs, work types)...");
await seeder.SeedAsync(passwordEncoder.Encode);

if (string.Equals(environment, "Development", StringComparison.OrdinalIgnoreCase))
{
    var demoPassword = Environment.GetEnvironmentVariable("SeedData__DemoPassword");
    var demoPasswordHash = string.IsNullOrEmpty(demoPassword)
        ? null
        : passwordEncoder.Encode(demoPassword);
    await seeder.SeedFromSqlAsync(demoPasswordHash);
    Console.WriteLine(demoPasswordHash is null
        ? "Development seed data applied (no demo accounts: SeedData__DemoPassword not set)."
        : "Development seed data and demo accounts applied.");
}
if (bootstrapAdminEnabled)
{
    await seeder.SeedDevelopmentAdminAsync(
        bootstrapAdminEmail!,
        passwordEncoder.Encode(bootstrapAdminPassword!));
    Console.WriteLine($"Development admin is ready for {bootstrapAdminEmail}.");
}
Console.WriteLine("Database migration and seed completed.");
