namespace CloudKeeperSN.Providers.GoogleDrive;

public sealed record GoogleOAuthConfiguration(string? ClientId, string? ClientSecret)
{
    public const string ReadOnlyScope = "https://www.googleapis.com/auth/drive.readonly";

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);

    public string? ValidationMessage => IsConfigured
        ? null
        : "Chưa cấu hình Google OAuth. Hãy đặt CLOUDKEEPERSN_GOOGLE_CLIENT_ID và CLOUDKEEPERSN_GOOGLE_CLIENT_SECRET cho OAuth Desktop application.";

    public static GoogleOAuthConfiguration FromEnvironment() => new(
        Environment.GetEnvironmentVariable("CLOUDKEEPERSN_GOOGLE_CLIENT_ID"),
        Environment.GetEnvironmentVariable("CLOUDKEEPERSN_GOOGLE_CLIENT_SECRET"));
}
