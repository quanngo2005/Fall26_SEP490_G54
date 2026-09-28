using G54.DAL;
using Microsoft.EntityFrameworkCore;

var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
    ?? "Host=localhost;Port=5432;Database=postgres;Username=postgres;Password=postgres";
var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options;
await using var dbContext = new AppDbContext(options);

Console.WriteLine("Applying database migrations...");
await dbContext.Database.MigrateAsync();
await new DatabaseSeeder(dbContext).SeedAsync();
Console.WriteLine("Database migration and seed completed.");
