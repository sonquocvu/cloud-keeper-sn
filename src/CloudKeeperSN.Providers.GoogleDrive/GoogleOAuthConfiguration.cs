using System.Text.Json;
using CloudKeeperSN.Application.Persistence;

namespace CloudKeeperSN.Providers.GoogleDrive;

public enum GoogleOAuthConfigurationStatus
{
    Missing,
    Invalid,
    Ready
}

public enum GoogleOAuthConfigurationSource
{
    None,
    ImportedSettings,
    Environment
}

public enum GoogleOAuthImportError
{
    EmptyFile,
    FileTooLarge,
    MalformedJson,
    WrongCredentialType,
    WebApplication,
    ServiceAccount,
    MissingInstalledObject,
    MissingClientId,
    MissingClientSecret,
    InvalidClientId,
    UnsafeAuthorizationEndpoint,
    UnsafeTokenEndpoint,
    UnsupportedRedirect,
    FileAccessDenied,
    FileReadFailed,
    SecureStorageFailed,
    ProtectedConfigurationInvalid,
    Unknown
}

public sealed class GoogleOAuthImportException(
    GoogleOAuthImportError error,
    string message,
    Exception? innerException = null) : Exception(message, innerException)
{
    public GoogleOAuthImportError Error { get; } = error;
}

public sealed class GoogleOAuthConfiguration
{
    public const string ClientIdEnvironmentVariable = "CLOUDKEEPERSN_GOOGLE_CLIENT_ID";
    public const string ClientSecretEnvironmentVariable = "CLOUDKEEPERSN_GOOGLE_CLIENT_SECRET";
    public const string ReadOnlyScope = "https://www.googleapis.com/auth/drive.readonly";

    internal GoogleOAuthConfiguration(
        string? clientId,
        string? clientSecret,
        GoogleOAuthConfigurationStatus status,
        GoogleOAuthConfigurationSource source,
        string validationMessage,
        DateTimeOffset? importedAtUtc = null)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
        Status = status;
        Source = source;
        ValidationMessage = validationMessage;
        ImportedAtUtc = importedAtUtc;
    }

    internal string? ClientId { get; }
    internal string? ClientSecret { get; }
    public GoogleOAuthConfigurationStatus Status { get; }
    public GoogleOAuthConfigurationSource Source { get; }
    public bool IsConfigured => Status == GoogleOAuthConfigurationStatus.Ready;
    public string ValidationMessage { get; }
    public DateTimeOffset? ImportedAtUtc { get; }

    public string SourceDescription => Source switch
    {
        GoogleOAuthConfigurationSource.ImportedSettings => "Đã nhập từ Cài đặt",
        GoogleOAuthConfigurationSource.Environment => "Cấu hình môi trường phát triển",
        _ => "Chưa cấu hình"
    };

    public string MaskedClientId => MaskClientId(ClientId);
    public string DiagnosticSummary =>
        $"status={Status}; source={Source}; imported={ImportedAtUtc is not null}; scope=drive.readonly";

    public override string ToString() => DiagnosticSummary;

    internal static string MaskClientId(string? clientId)
    {
        if (string.IsNullOrWhiteSpace(clientId)) return string.Empty;
        const string suffix = ".apps.googleusercontent.com";
        var prefixLength = Math.Min(4, Math.Max(0, clientId.Length - suffix.Length));
        return clientId[..prefixLength] + "••••••••" + suffix;
    }

    internal static bool IsPlausibleClientId(string value) =>
        value.EndsWith(".apps.googleusercontent.com", StringComparison.OrdinalIgnoreCase) &&
        value.Length > ".apps.googleusercontent.com".Length &&
        !value.Any(char.IsWhiteSpace);

    internal static bool IsPlaceholder(string value)
    {
        var normalized = value.Trim().Replace('-', '_').ToUpperInvariant();
        return normalized is "..." or "CLIENT_ID" or "CLIENT_SECRET" or
               "YOUR_CLIENT_ID" or "YOUR_CLIENT_SECRET" or
               "YOUR_GOOGLE_DESKTOP_CLIENT_ID" or "YOUR_GOOGLE_DESKTOP_CLIENT_SECRET" ||
               normalized.Contains("PLACEHOLDER", StringComparison.Ordinal);
    }
}

public sealed record GoogleOAuthConfigurationMetadata(
    GoogleOAuthConfigurationStatus Status,
    string StatusText,
    string SourceLabel,
    string MaskedClientId,
    DateTimeOffset? ImportedAtUtc,
    string ValidationMessage,
    bool CanRemoveImportedConfiguration)
{
    internal static GoogleOAuthConfigurationMetadata FromConfiguration(GoogleOAuthConfiguration configuration) => new(
        configuration.Status,
        configuration.Status switch
        {
            GoogleOAuthConfigurationStatus.Ready => "Đã cấu hình",
            GoogleOAuthConfigurationStatus.Invalid => "Cấu hình không hợp lệ",
            _ => "Chưa cấu hình"
        },
        configuration.SourceDescription,
        configuration.MaskedClientId,
        configuration.ImportedAtUtc,
        configuration.ValidationMessage,
        configuration.Source == GoogleOAuthConfigurationSource.ImportedSettings);
}

public sealed class GoogleOAuthImportCandidate
{
    internal GoogleOAuthImportCandidate(string clientId, string clientSecret)
    {
        ClientId = clientId;
        ClientSecret = clientSecret;
    }

    internal string ClientId { get; }
    internal string ClientSecret { get; }
    public string ApplicationType => "Ứng dụng máy tính";
    public string MaskedClientId => GoogleOAuthConfiguration.MaskClientId(ClientId);
    public override string ToString() => $"GoogleDesktopOAuth({MaskedClientId})";
}

public interface IGoogleOAuthEnvironment
{
    string? GetValue(string variableName);
}

public interface IGoogleOAuthImportFileReader
{
    Task<byte[]> ReadAsync(string path, long maximumBytes, CancellationToken cancellationToken);
}

public interface IGoogleOAuthClock
{
    DateTimeOffset UtcNow { get; }
}

public interface IGoogleOAuthConfigurationManager
{
    GoogleOAuthConfigurationMetadata Current { get; }
    event Action<GoogleOAuthConfigurationMetadata>? Changed;
    Task InitializeAsync(CancellationToken cancellationToken);
    Task<GoogleOAuthImportCandidate> ValidateImportAsync(string sourcePath, CancellationToken cancellationToken);
    Task ImportAsync(GoogleOAuthImportCandidate candidate, CancellationToken cancellationToken);
    Task RemoveImportedAsync(CancellationToken cancellationToken);
}

public sealed class SystemGoogleOAuthEnvironment : IGoogleOAuthEnvironment
{
    public string? GetValue(string variableName)
    {
        var processValue = Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.Process);
        if (!string.IsNullOrWhiteSpace(processValue)) return processValue;
        try
        {
            return Environment.GetEnvironmentVariable(variableName, EnvironmentVariableTarget.User);
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }
}

public sealed class GoogleOAuthImportFileReader : IGoogleOAuthImportFileReader
{
    public async Task<byte[]> ReadAsync(string path, long maximumBytes, CancellationToken cancellationToken)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists)
                throw new GoogleOAuthImportException(
                    GoogleOAuthImportError.FileReadFailed,
                    "Không tìm thấy file OAuth đã chọn. Cấu hình hiện tại vẫn được giữ; hãy chọn lại file JSON đã tải từ Google Cloud Console.");
            if (file.Length > maximumBytes)
                throw new GoogleOAuthImportException(
                    GoogleOAuthImportError.FileTooLarge,
                    "File OAuth quá lớn và không được đọc. Cấu hình hiện tại vẫn được giữ; hãy chọn đúng file JSON OAuth Desktop do Google tạo.");
            return await File.ReadAllBytesAsync(path, cancellationToken);
        }
        catch (OperationCanceledException) { throw; }
        catch (GoogleOAuthImportException) { throw; }
        catch (UnauthorizedAccessException exception)
        {
            throw new GoogleOAuthImportException(
                GoogleOAuthImportError.FileAccessDenied,
                "CloudKeeperSN không có quyền đọc file đã chọn. Cấu hình hiện tại vẫn được giữ; hãy kiểm tra quyền file hoặc chọn một bản sao có thể đọc.",
                exception);
        }
        catch (IOException exception)
        {
            throw new GoogleOAuthImportException(
                GoogleOAuthImportError.FileReadFailed,
                "Không thể đọc file OAuth đã chọn. Cấu hình hiện tại vẫn được giữ; hãy đóng ứng dụng đang dùng file rồi thử lại.",
                exception);
        }
    }
}

public sealed class SystemGoogleOAuthClock : IGoogleOAuthClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}

public sealed class GoogleDesktopOAuthJsonParser
{
    public const long MaximumFileBytes = 1024 * 1024;

    public GoogleOAuthImportCandidate Parse(ReadOnlyMemory<byte> utf8Json)
    {
        if (utf8Json.IsEmpty || IsOnlyWhitespace(utf8Json.Span))
            throw Error(GoogleOAuthImportError.EmptyFile,
                "File OAuth đang trống. Cấu hình hiện tại vẫn được giữ; hãy tải lại file OAuth Desktop từ Google Cloud Console.");
        if (utf8Json.Length > MaximumFileBytes)
            throw Error(GoogleOAuthImportError.FileTooLarge,
                "File OAuth quá lớn. Cấu hình hiện tại vẫn được giữ; hãy chọn đúng file JSON OAuth Desktop do Google tạo.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(utf8Json, new JsonDocumentOptions
            {
                AllowTrailingCommas = false,
                CommentHandling = JsonCommentHandling.Disallow,
                MaxDepth = 16
            });
        }
        catch (JsonException exception)
        {
            throw new GoogleOAuthImportException(
                GoogleOAuthImportError.MalformedJson,
                "File đã chọn không phải JSON hợp lệ. Cấu hình hiện tại vẫn được giữ; hãy tải lại file OAuth Desktop từ Google Cloud Console.",
                exception);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Error(GoogleOAuthImportError.WrongCredentialType, WrongTypeMessage);
            if (root.TryGetProperty("web", out _))
                throw Error(GoogleOAuthImportError.WebApplication, WrongTypeMessage);
            if (IsServiceAccount(root))
                throw Error(GoogleOAuthImportError.ServiceAccount, WrongTypeMessage);
            if (!root.TryGetProperty("installed", out var installed) || installed.ValueKind != JsonValueKind.Object)
                throw Error(GoogleOAuthImportError.MissingInstalledObject, WrongTypeMessage);

            var clientId = RequiredString(installed, "client_id", GoogleOAuthImportError.MissingClientId,
                "File OAuth Desktop thiếu client_id. Cấu hình hiện tại vẫn được giữ; hãy tải file JSON mới từ Google Cloud Console.");
            var clientSecret = RequiredString(installed, "client_secret", GoogleOAuthImportError.MissingClientSecret,
                "File OAuth Desktop thiếu client_secret. Cấu hình hiện tại vẫn được giữ; hãy tải file JSON mới từ Google Cloud Console.");
            if (GoogleOAuthConfiguration.IsPlaceholder(clientId) || !GoogleOAuthConfiguration.IsPlausibleClientId(clientId))
                throw Error(GoogleOAuthImportError.InvalidClientId,
                    "Client ID trong file không đúng định dạng Google OAuth. Cấu hình hiện tại vẫn được giữ; hãy tạo Client ID loại “Desktop app” và tải file JSON mới.");
            if (GoogleOAuthConfiguration.IsPlaceholder(clientSecret))
                throw Error(GoogleOAuthImportError.MissingClientSecret,
                    "Client secret trong file là giá trị mẫu. Cấu hình hiện tại vẫn được giữ; hãy chọn file JSON thật được tải từ Google Cloud Console.");

            var authUri = RequiredString(installed, "auth_uri", GoogleOAuthImportError.UnsafeAuthorizationEndpoint,
                "File OAuth thiếu địa chỉ xác thực an toàn. Cấu hình hiện tại vẫn được giữ; hãy tải file OAuth Desktop mới từ Google.");
            if (!IsAllowedEndpoint(authUri, "accounts.google.com", "/o/oauth2/auth", "/o/oauth2/v2/auth"))
                throw Error(GoogleOAuthImportError.UnsafeAuthorizationEndpoint,
                    "Địa chỉ xác thực trong file không phải endpoint HTTPS của Google. Cấu hình hiện tại vẫn được giữ; không sử dụng file này.");

            var tokenUri = RequiredString(installed, "token_uri", GoogleOAuthImportError.UnsafeTokenEndpoint,
                "File OAuth thiếu địa chỉ cấp token an toàn. Cấu hình hiện tại vẫn được giữ; hãy tải file OAuth Desktop mới từ Google.");
            if (!IsAllowedEndpoint(tokenUri, "oauth2.googleapis.com", "/token"))
                throw Error(GoogleOAuthImportError.UnsafeTokenEndpoint,
                    "Địa chỉ cấp token trong file không phải endpoint HTTPS của Google. Cấu hình hiện tại vẫn được giữ; không sử dụng file này.");

            if (!HasOnlySupportedRedirects(installed))
                throw Error(GoogleOAuthImportError.UnsupportedRedirect,
                    "File OAuth không có callback localhost hợp lệ cho ứng dụng máy tính. Cấu hình hiện tại vẫn được giữ; hãy tạo Client ID loại “Desktop app” rồi tải file JSON mới.");

            return new GoogleOAuthImportCandidate(clientId, clientSecret);
        }
    }

    private static string RequiredString(JsonElement parent, string name, GoogleOAuthImportError error, string message)
    {
        if (!parent.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(value.GetString()))
            throw Error(error, message);
        return value.GetString()!.Trim();
    }

    private static bool IsAllowedEndpoint(string value, string host, params string[] paths) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase) &&
        uri.IsDefaultPort &&
        paths.Contains(uri.AbsolutePath, StringComparer.Ordinal) &&
        string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment);

    private static bool HasOnlySupportedRedirects(JsonElement installed)
    {
        if (!installed.TryGetProperty("redirect_uris", out var redirects) || redirects.ValueKind != JsonValueKind.Array)
            return false;
        var count = 0;
        foreach (var value in redirects.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.String || !Uri.TryCreate(value.GetString()?.Trim(), UriKind.Absolute, out var uri))
                return false;
            if (uri.Scheme != Uri.UriSchemeHttp || !uri.IsLoopback || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
                return false;
            count++;
        }
        return count > 0;
    }

    private static bool IsServiceAccount(JsonElement root) =>
        root.TryGetProperty("type", out var type) &&
        type.ValueKind == JsonValueKind.String &&
        string.Equals(type.GetString(), "service_account", StringComparison.OrdinalIgnoreCase) ||
        root.TryGetProperty("private_key", out _) || root.TryGetProperty("client_email", out _);

    private static bool IsOnlyWhitespace(ReadOnlySpan<byte> bytes)
    {
        foreach (var value in bytes)
            if (value is not (9 or 10 or 13 or 32)) return false;
        return true;
    }

    private static GoogleOAuthImportException Error(GoogleOAuthImportError error, string message) => new(error, message);

    private const string WrongTypeMessage =
        "File đã chọn không phải OAuth dành cho ứng dụng máy tính. Cấu hình hiện tại vẫn được giữ. Trong Google Cloud Console, hãy tạo OAuth Client ID với loại “Desktop app” rồi tải file JSON mới.";
}

public sealed class GoogleOAuthConfigurationManager : IGoogleOAuthConfigurationManager
{
    private const string StorageProvider = "google-oauth-config";
    private const string StorageKey = "desktop-client-v1";
    private readonly IProtectedCredentialStore _credentials;
    private readonly IGoogleOAuthEnvironment _environment;
    private readonly IGoogleOAuthImportFileReader _files;
    private readonly IGoogleOAuthClock _clock;
    private readonly IProviderDiagnostics _diagnostics;
    private readonly GoogleDesktopOAuthJsonParser _parser = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private GoogleOAuthConfiguration _configuration = Missing();

    public GoogleOAuthConfigurationManager(
        IProtectedCredentialStore credentials,
        IGoogleOAuthEnvironment environment,
        IGoogleOAuthImportFileReader files,
        IGoogleOAuthClock clock,
        IProviderDiagnostics diagnostics)
    {
        _credentials = credentials;
        _environment = environment;
        _files = files;
        _clock = clock;
        _diagnostics = diagnostics;
    }

    public GoogleOAuthConfigurationMetadata Current => GoogleOAuthConfigurationMetadata.FromConfiguration(_configuration);
    internal GoogleOAuthConfiguration EffectiveConfiguration => _configuration;
    public event Action<GoogleOAuthConfigurationMetadata>? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                var bytes = await _credentials.GetAsync(StorageProvider, StorageKey, cancellationToken);
                if (bytes is not null)
                {
                    var stored = JsonSerializer.Deserialize<StoredConfiguration>(bytes);
                    if (stored is null || !IsValidStored(stored))
                        throw new JsonException("Stored Google OAuth configuration is invalid.");
                    SetConfiguration(ReadyImported(stored.ClientId!, stored.ClientSecret!, stored.ImportedAtUtc));
                    await WriteDiagnosticAsync("GoogleOAuthConfigurationRestored", "Đã khôi phục cấu hình Google OAuth được bảo vệ.", cancellationToken);
                    return;
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is ProtectedCredentialException or JsonException)
            {
                SetConfiguration(new GoogleOAuthConfiguration(
                    null, null, GoogleOAuthConfigurationStatus.Invalid, GoogleOAuthConfigurationSource.ImportedSettings,
                    "Cấu hình OAuth không hợp lệ vì dữ liệu được bảo vệ không thể khôi phục. Hãy nhập lại file OAuth Desktop trong Cài đặt."));
                try
                {
                    await _diagnostics.WriteAsync(
                        "GoogleOAuthConfigurationRestoreFailed",
                        "Không thể khôi phục cấu hình Google OAuth được bảo vệ; cần nhập lại.",
                        $"category={GoogleOAuthImportError.ProtectedConfigurationInvalid}; exception={exception.GetType().Name}",
                        CancellationToken.None);
                }
                catch { }
                return;
            }

            SetConfiguration(LoadEnvironment());
            await WriteDiagnosticAsync("GoogleOAuthConfigurationLoaded", "Đã kiểm tra nguồn cấu hình Google OAuth.", cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<GoogleOAuthImportCandidate> ValidateImportAsync(string sourcePath, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        var bytes = await _files.ReadAsync(sourcePath, GoogleDesktopOAuthJsonParser.MaximumFileBytes, cancellationToken);
        return _parser.Parse(bytes);
    }

    public async Task ImportAsync(GoogleOAuthImportCandidate candidate, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var importedAt = _clock.UtcNow;
            var payload = JsonSerializer.SerializeToUtf8Bytes(new StoredConfiguration
            {
                ClientId = candidate.ClientId,
                ClientSecret = candidate.ClientSecret,
                ImportedAtUtc = importedAt
            });
            try
            {
                await _credentials.StoreAsync(StorageProvider, StorageKey, payload, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is ProtectedCredentialException or IOException or UnauthorizedAccessException)
            {
                throw new GoogleOAuthImportException(
                    GoogleOAuthImportError.SecureStorageFailed,
                    "Không thể lưu cấu hình OAuth bằng bộ nhớ bảo vệ của Windows. Cấu hình hiện tại vẫn được giữ; hãy kiểm tra quyền thư mục dữ liệu ứng dụng rồi thử lại.",
                    exception);
            }

            SetConfiguration(ReadyImported(candidate.ClientId, candidate.ClientSecret, importedAt));
            await WriteDiagnosticAsync("GoogleOAuthConfigurationImported", "Đã nhập cấu hình Google OAuth từ Cài đặt.", cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveImportedAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            try
            {
                await _credentials.DeleteAsync(StorageProvider, StorageKey, cancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception exception) when (exception is ProtectedCredentialException or IOException or UnauthorizedAccessException)
            {
                throw new GoogleOAuthImportException(
                    GoogleOAuthImportError.SecureStorageFailed,
                    "Không thể xóa cấu hình OAuth được bảo vệ. Cấu hình hiện tại chưa thay đổi; hãy kiểm tra quyền dữ liệu ứng dụng rồi thử lại.",
                    exception);
            }

            SetConfiguration(LoadEnvironment());
            await WriteDiagnosticAsync("GoogleOAuthConfigurationRemoved", "Đã xóa cấu hình Google OAuth đã nhập khỏi máy này.", cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private GoogleOAuthConfiguration LoadEnvironment()
    {
        var clientId = Normalize(_environment.GetValue(GoogleOAuthConfiguration.ClientIdEnvironmentVariable));
        var clientSecret = Normalize(_environment.GetValue(GoogleOAuthConfiguration.ClientSecretEnvironmentVariable));
        if (clientId is null && clientSecret is null) return Missing();
        if (clientId is null || clientSecret is null)
        {
            var missing = clientId is null
                ? GoogleOAuthConfiguration.ClientIdEnvironmentVariable
                : GoogleOAuthConfiguration.ClientSecretEnvironmentVariable;
            return new GoogleOAuthConfiguration(null, null, GoogleOAuthConfigurationStatus.Invalid, GoogleOAuthConfigurationSource.Environment,
                $"Cấu hình môi trường phát triển không đầy đủ: thiếu {missing}. Hãy nhập file OAuth Desktop trong Cài đặt hoặc cung cấp đủ cả hai biến môi trường.");
        }
        if (GoogleOAuthConfiguration.IsPlaceholder(clientId) || !GoogleOAuthConfiguration.IsPlausibleClientId(clientId) ||
            GoogleOAuthConfiguration.IsPlaceholder(clientSecret))
            return new GoogleOAuthConfiguration(null, null, GoogleOAuthConfigurationStatus.Invalid, GoogleOAuthConfigurationSource.Environment,
                "Cấu hình môi trường phát triển không hợp lệ. Hãy kiểm tra lại cặp Client ID/secret hoặc nhập file OAuth Desktop trong Cài đặt.");
        return new GoogleOAuthConfiguration(clientId, clientSecret, GoogleOAuthConfigurationStatus.Ready, GoogleOAuthConfigurationSource.Environment,
            "OAuth đã sẵn sàng (nguồn: Cấu hình môi trường phát triển). Chưa kết nối tài khoản.");
    }

    private void SetConfiguration(GoogleOAuthConfiguration configuration)
    {
        _configuration = configuration;
        Changed?.Invoke(Current);
    }

    private async Task WriteDiagnosticAsync(string eventType, string message, CancellationToken cancellationToken)
    {
        try
        {
            await _diagnostics.WriteAsync(eventType, message, _configuration.DiagnosticSummary, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch { }
    }

    private static bool IsValidStored(StoredConfiguration stored) =>
        stored.ImportedAtUtc != default &&
        !string.IsNullOrWhiteSpace(stored.ClientId) &&
        !string.IsNullOrWhiteSpace(stored.ClientSecret) &&
        !GoogleOAuthConfiguration.IsPlaceholder(stored.ClientId) &&
        GoogleOAuthConfiguration.IsPlausibleClientId(stored.ClientId) &&
        !GoogleOAuthConfiguration.IsPlaceholder(stored.ClientSecret);

    private static GoogleOAuthConfiguration ReadyImported(string clientId, string clientSecret, DateTimeOffset importedAtUtc) =>
        new(clientId, clientSecret, GoogleOAuthConfigurationStatus.Ready, GoogleOAuthConfigurationSource.ImportedSettings,
            "OAuth đã sẵn sàng (nguồn: Đã nhập từ Cài đặt). Chưa kết nối tài khoản.", importedAtUtc);

    private static GoogleOAuthConfiguration Missing() =>
        new(null, null, GoogleOAuthConfigurationStatus.Missing, GoogleOAuthConfigurationSource.None,
            "Chưa cấu hình OAuth. Mở Cài đặt > Kết nối Google Drive và chọn file OAuth JSON dành cho ứng dụng máy tính.");

    private static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private sealed class StoredConfiguration
    {
        public string? ClientId { get; init; }
        public string? ClientSecret { get; init; }
        public DateTimeOffset ImportedAtUtc { get; init; }
        public override string ToString() => "ProtectedGoogleOAuthConfiguration";
    }
}
