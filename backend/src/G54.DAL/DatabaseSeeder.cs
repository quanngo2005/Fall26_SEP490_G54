using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public sealed class DatabaseSeeder(AppDbContext dbContext)
{
    private readonly AppDbContext _dbContext = dbContext;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        // Seeder can be expanded with initial Progest seed data
        if (await _dbContext.Database.CanConnectAsync(cancellationToken))
        {
            // Database connection verified
        }
    }
}
