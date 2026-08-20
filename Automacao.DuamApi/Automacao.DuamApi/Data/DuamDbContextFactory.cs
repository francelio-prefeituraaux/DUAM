using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DuamApi.Data;

/// <summary>
/// Usado apenas pelas ferramentas de design-time (dotnet ef migrations ...).
/// Evita que a geração de migrações precise de um MySQL real disponível
/// (diferente do Program.cs, que usa ServerVersion.AutoDetect em runtime).
/// </summary>
public class DuamDbContextFactory : IDesignTimeDbContextFactory<DuamDbContext>
{
    public DuamDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DuamMySql")
            ?? "Server=localhost;Port=3306;Database=duam_api;User=duam_api;Password=duam_api;";

        var optionsBuilder = new DbContextOptionsBuilder<DuamDbContext>();
        optionsBuilder.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 0)));

        return new DuamDbContext(optionsBuilder.Options);
    }
}
