using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Infrastructure.Persistence;
using Microsoft.Extensions.DependencyInjection;

namespace CloudKeeperSN.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCloudKeeperInfrastructure(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new SqliteOptions(databasePath));
        services.AddSingleton<SqliteConnectionFactory>();
        services.AddSingleton<IApplicationDatabase, SqliteApplicationDatabase>();
        services.AddSingleton<IStorageAccountRepository, SqliteStorageAccountRepository>();
        services.AddSingleton<ITransferMappingRepository, SqliteTransferMappingRepository>();
        services.AddSingleton<ITransferItemRepository, SqliteTransferItemRepository>();
        services.AddSingleton<IActivityEventRepository, SqliteActivityEventRepository>();
        services.AddSingleton<IApplicationSettingRepository, SqliteApplicationSettingRepository>();
        return services;
    }
}
