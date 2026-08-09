using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Infrastructure.Persistence;
using CloudKeeperSN.Infrastructure.Security;
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
        services.AddSingleton<IDriveInventoryRepository, SqliteDriveInventoryRepository>();
        services.AddSingleton<ITransferMappingRepository, SqliteTransferMappingRepository>();
        services.AddSingleton<ITransferItemRepository, SqliteTransferItemRepository>();
        services.AddSingleton<IActivityEventRepository, SqliteActivityEventRepository>();
        services.AddSingleton<IApplicationSettingRepository, SqliteApplicationSettingRepository>();
        services.AddSingleton<ICredentialProtector, DpapiCredentialProtector>();
        services.AddSingleton<IProtectedCredentialStore>(provider => new FileProtectedCredentialStore(
            provider.GetRequiredService<ICredentialProtector>(),
            Path.Combine(Path.GetDirectoryName(databasePath) ?? throw new ArgumentException("Database path must include a directory.", nameof(databasePath)), "Credentials")));
        services.AddSingleton<IProviderDiagnostics, ActivityProviderDiagnostics>();
        return services;
    }
}
