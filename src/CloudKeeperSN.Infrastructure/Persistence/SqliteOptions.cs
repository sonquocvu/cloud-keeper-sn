namespace CloudKeeperSN.Infrastructure.Persistence;

public sealed record SqliteOptions(string DatabasePath)
{
    public string ConnectionString => $"Data Source={DatabasePath};Mode=ReadWriteCreate;Cache=Shared;Foreign Keys=True;Default Timeout=15";
}

