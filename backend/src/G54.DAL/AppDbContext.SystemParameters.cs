using G54.DAL.Entities;
using Microsoft.EntityFrameworkCore;

namespace G54.DAL;

public partial class AppDbContext
{
    public DbSet<SystemParameter> SystemParameters => Set<SystemParameter>();
    public DbSet<SystemParameterChange> SystemParameterChanges => Set<SystemParameterChange>();
}
