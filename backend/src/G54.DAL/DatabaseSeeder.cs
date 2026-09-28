using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public sealed class DatabaseSeeder(AppDbContext dbContext)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.Users.Add(new AppUser
        {
            Email = "admin@g54.local",
            PasswordHash = "CHANGE_ME_BEFORE_IMPLEMENTING_AUTH",
            Role = "Admin",
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }
}
