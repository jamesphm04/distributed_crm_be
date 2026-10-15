using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DistributedCrm.Infrastructure.Persistence;

// Used only by the dotnet-ef tools (migrations add / script), never at runtime. Without it the tools run the API's
// Program.cs to get a DbContext, which only works where its full configuration is present. Generating migrations and
// scripts needs the model and the provider, not a database, so no connection string is set.
public sealed class DistributedCrmDbContextFactory : IDesignTimeDbContextFactory<DistributedCrmDbContext>
{
    public DistributedCrmDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<DistributedCrmDbContext>()
            .UseNpgsql()
            .UseSnakeCaseNamingConvention()
            .Options;

        return new DistributedCrmDbContext(options);
    }
}
