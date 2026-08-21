using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Sergin.MeterMinder.DeviceManagement.Infrastructure.Data;

internal sealed class DeviceManagementDbContextDesignTimeFactory() : IDesignTimeDbContextFactory<DeviceManagementDbContext>
{
    public DeviceManagementDbContext CreateDbContext(string[] args)
    {
        IConfigurationRoot configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Development.json", false, false)
            .Build();

        string connectionString = configuration.GetSection("Sergin").GetConnectionString("Database");

        var optionBuilder = new DbContextOptionsBuilder<DeviceManagementDbContext>();

        optionBuilder
            .UseNpgsql(connectionString,
                    sqlOptions => sqlOptions
                    .MigrationsHistoryTable(HistoryRepository.DefaultTableName, DeviceManagementDbContext.Schema))
            .UseSnakeCaseNamingConvention();

        return new DeviceManagementDbContext(optionBuilder.Options);
    }
}
