using System.Text;
using CloudKeeperSN.Application.Persistence;
using CloudKeeperSN.Providers.GoogleDrive;
using CloudKeeperSN.Providers.GoogleDrive.Authentication;

namespace CloudKeeperSN.Application.Tests;

public sealed class GoogleOAuthConfigurationTests
{
    private const string ClientId = "123456789-example.apps.googleusercontent.com";
    private const string ClientSecret = "test-secret-value";

    [Fact]
    public void ValidDesktopJsonReturnsOnlySafePublicMetadata()
    {
        var candidate = Parse(ValidJson());

        Assert.Equal("Ứng dụng máy tính", candidate.ApplicationType);
        Assert.Equal("1234••••••••.apps.googleusercontent.com", candidate.MaskedClientId);
        Assert.DoesNotContain(ClientSecret, candidate.ToString());
    }

    [Theory]
    [InlineData("", GoogleOAuthImportError.EmptyFile)]
    [InlineData("   \r\n\t", GoogleOAuthImportError.EmptyFile)]
    [InlineData("{ invalid", GoogleOAuthImportError.MalformedJson)]
    [InlineData("[]", GoogleOAuthImportError.WrongCredentialType)]
    [InlineData("{}", GoogleOAuthImportError.MissingInstalledObject)]
    [InlineData("{\"web\":{}}", GoogleOAuthImportError.WebApplication)]
    [InlineData("{\"type\":\"service_account\",\"private_key\":\"secret\"}", GoogleOAuthImportError.ServiceAccount)]
    public void WrongJsonShapesAreRejectedSafely(string json, GoogleOAuthImportError expected)
    {
        var exception = Assert.Throws<GoogleOAuthImportException>(() => Parse(json));

        Assert.Equal(expected, exception.Error);
        Assert.DoesNotContain("private_key", exception.Message);
        Assert.Contains("Cấu hình hiện tại vẫn được giữ", exception.Message);
    }

    [Fact]
    public void OversizedJsonIsRejectedBeforeParsing()
    {
        var bytes = new byte[GoogleDesktopOAuthJsonParser.MaximumFileBytes + 1];

        var exception = Assert.Throws<GoogleOAuthImportException>(() => new GoogleDesktopOAuthJsonParser().Parse(bytes));

        Assert.Equal(GoogleOAuthImportError.FileTooLarge, exception.Error);
    }

    [Theory]
    [InlineData(null, "secret", GoogleOAuthImportError.MissingClientId)]
    [InlineData("123.apps.googleusercontent.com", null, GoogleOAuthImportError.MissingClientSecret)]
    [InlineData("not-google", "secret", GoogleOAuthImportError.InvalidClientId)]
    public void RequiredClientValuesAreValidated(string? clientId, string? secret, GoogleOAuthImportError expected)
    {
        var exception = Assert.Throws<GoogleOAuthImportException>(() => Parse(ValidJson(clientId, secret)));

        Assert.Equal(expected, exception.Error);
        if (secret is not null) Assert.DoesNotContain(secret, exception.Message);
    }

    [Theory]
    [InlineData("http://accounts.google.com/o/oauth2/auth", "https://oauth2.googleapis.com/token", GoogleOAuthImportError.UnsafeAuthorizationEndpoint)]
    [InlineData("https://evil.example/o/oauth2/auth", "https://oauth2.googleapis.com/token", GoogleOAuthImportError.UnsafeAuthorizationEndpoint)]
    [InlineData("https://accounts.google.com/o/oauth2/auth", "http://oauth2.googleapis.com/token", GoogleOAuthImportError.UnsafeTokenEndpoint)]
    [InlineData("https://accounts.google.com/o/oauth2/auth", "https://evil.example/token", GoogleOAuthImportError.UnsafeTokenEndpoint)]
    public void UnsafeEndpointsAreRejected(string authUri, string tokenUri, GoogleOAuthImportError expected)
    {
        var exception = Assert.Throws<GoogleOAuthImportException>(() => Parse(ValidJson(authUri: authUri, tokenUri: tokenUri)));
        Assert.Equal(expected, exception.Error);
    }

    [Theory]
    [InlineData("https://localhost")]
    [InlineData("http://example.com")]
    [InlineData("urn:ietf:wg:oauth:2.0:oob")]
    public void UnsupportedRedirectIsRejected(string redirect)
    {
        var exception = Assert.Throws<GoogleOAuthImportException>(() => Parse(ValidJson(redirect: redirect)));
        Assert.Equal(GoogleOAuthImportError.UnsupportedRedirect, exception.Error);
    }

    [Theory]
    [InlineData("http://localhost")]
    [InlineData("http://127.0.0.1:45678/authorize/")]
    [InlineData("http://[::1]")]
    public void OfficialLoopbackRedirectsAreAccepted(string redirect)
    {
        var candidate = Parse(ValidJson(redirect: redirect));
        Assert.Equal("Ứng dụng máy tính", candidate.ApplicationType);
    }

    [Fact]
    public void UnknownFieldsUnicodeAndWhitespaceAreHandled()
    {
        var json = ValidJson(
            clientId: $"  {ClientId}  ",
            secret: $"  {ClientSecret}  ",
            extra: ",\"project_id\":\"dự-án-thử\",\"unknown\":{\"nested\":true}");

        var candidate = Parse(json);

        Assert.Equal("1234••••••••.apps.googleusercontent.com", candidate.MaskedClientId);
    }

    [Fact]
    public async Task ImportedConfigurationWinsAndIsAvailableImmediately()
    {
        var store = new MemoryCredentialStore();
        var diagnostics = new MemoryDiagnostics();
        var manager = CreateManager(store, diagnostics, environment: new Dictionary<string, string?>
        {
            [GoogleOAuthConfiguration.ClientIdEnvironmentVariable] = "environment.apps.googleusercontent.com",
            [GoogleOAuthConfiguration.ClientSecretEnvironmentVariable] = "environment-secret"
        });
        await manager.InitializeAsync(CancellationToken.None);
        var changes = 0;
        manager.Changed += _ => changes++;

        var candidate = await manager.ValidateImportAsync("download.json", CancellationToken.None);
        await manager.ImportAsync(candidate, CancellationToken.None);

        Assert.Equal(GoogleOAuthConfigurationStatus.Ready, manager.Current.Status);
        Assert.Equal("Đã nhập từ Cài đặt", manager.Current.SourceLabel);
        Assert.Equal("1234••••••••.apps.googleusercontent.com", manager.Current.MaskedClientId);
        Assert.True(manager.Current.CanRemoveImportedConfiguration);
        Assert.Equal(1, changes);
        Assert.DoesNotContain(ClientSecret, manager.Current.ToString());
        Assert.DoesNotContain(diagnostics.Events, entry => entry.Contains(ClientSecret, StringComparison.Ordinal));
        var protectedStorePayload = Encoding.UTF8.GetString(store.Stored!);
        Assert.DoesNotContain("download.json", protectedStorePayload);
        Assert.DoesNotContain("auth_uri", protectedStorePayload);
    }

    [Fact]
    public async Task RuntimeImportImmediatelyRefreshesOAuthClient()
    {
        var store = new MemoryCredentialStore();
        var manager = CreateManager(store);
        await using var client = new GoogleApisOAuthClient(manager, store);
        await manager.InitializeAsync(CancellationToken.None);
        var notifications = 0;
        client.ConfigurationChanged += () => notifications++;

        await manager.ImportAsync(await manager.ValidateImportAsync("download.json", CancellationToken.None), CancellationToken.None);

        Assert.True(client.IsConfigured);
        Assert.Equal(1, notifications);
        Assert.Contains("Đã nhập từ Cài đặt", client.ConfigurationMessage);
    }

    [Fact]
    public async Task ClearingOldAccountAuthorizationDoesNotRemoveImportedClientConfiguration()
    {
        var store = new MemoryCredentialStore();
        var manager = CreateManager(store);
        await using var client = new GoogleApisOAuthClient(manager, store);
        await manager.InitializeAsync(CancellationToken.None);
        await manager.ImportAsync(await manager.ValidateImportAsync("download.json", CancellationToken.None), CancellationToken.None);
        await store.StoreAsync("google-drive", "old-token", Encoding.UTF8.GetBytes("old-token-value"), CancellationToken.None);

        await client.ClearLocalAuthorizationAsync(CancellationToken.None);

        Assert.False(store.HasProvider("google-drive"));
        Assert.True(store.HasProvider("google-oauth-config"));
        var restarted = CreateManager(store, fileContent: null);
        await restarted.InitializeAsync(CancellationToken.None);
        Assert.Equal(GoogleOAuthConfigurationStatus.Ready, restarted.Current.Status);
    }

    [Fact]
    public async Task RestartRestoresImportedConfigurationWithoutOriginalFile()
    {
        var store = new MemoryCredentialStore();
        var first = CreateManager(store);
        await first.InitializeAsync(CancellationToken.None);
        await first.ImportAsync(await first.ValidateImportAsync("download.json", CancellationToken.None), CancellationToken.None);

        var restarted = CreateManager(store, fileContent: null);
        await restarted.InitializeAsync(CancellationToken.None);

        Assert.Equal(GoogleOAuthConfigurationStatus.Ready, restarted.Current.Status);
        Assert.Equal("Đã nhập từ Cài đặt", restarted.Current.SourceLabel);
        Assert.Equal("1234••••••••.apps.googleusercontent.com", restarted.Current.MaskedClientId);
    }

    [Fact]
    public async Task InvalidImportDoesNotReplaceWorkingConfiguration()
    {
        var store = new MemoryCredentialStore();
        var manager = CreateManager(store);
        await manager.InitializeAsync(CancellationToken.None);
        await manager.ImportAsync(await manager.ValidateImportAsync("download.json", CancellationToken.None), CancellationToken.None);
        var before = store.Snapshot();
        var invalidManager = CreateManager(store, fileContent: "{ invalid");
        await invalidManager.InitializeAsync(CancellationToken.None);

        await Assert.ThrowsAsync<GoogleOAuthImportException>(() =>
            invalidManager.ValidateImportAsync("invalid.json", CancellationToken.None));

        Assert.Equal(before, store.Snapshot());
        Assert.Equal(GoogleOAuthConfigurationStatus.Ready, invalidManager.Current.Status);
    }

    [Fact]
    public async Task PersistenceFailureKeepsPreviousConfiguration()
    {
        var store = new MemoryCredentialStore();
        var manager = CreateManager(store);
        await manager.InitializeAsync(CancellationToken.None);
        await manager.ImportAsync(await manager.ValidateImportAsync("first.json", CancellationToken.None), CancellationToken.None);
        var before = manager.Current;
        store.FailStore = true;

        var exception = await Assert.ThrowsAsync<GoogleOAuthImportException>(() =>
            manager.ImportAsync(Parse(ValidJson("replacement.apps.googleusercontent.com", "replacement-secret")), CancellationToken.None));

        Assert.Equal(GoogleOAuthImportError.SecureStorageFailed, exception.Error);
        Assert.Equal(before, manager.Current);
    }

    [Fact]
    public async Task CorruptedProtectedConfigurationIsRecoverable()
    {
        var store = new MemoryCredentialStore { Stored = Encoding.UTF8.GetBytes("not-json") };
        var manager = CreateManager(store);

        await manager.InitializeAsync(CancellationToken.None);

        Assert.Equal(GoogleOAuthConfigurationStatus.Invalid, manager.Current.Status);
        Assert.True(manager.Current.CanRemoveImportedConfiguration);
        Assert.Contains("nhập lại", manager.Current.ValidationMessage);
    }

    [Fact]
    public async Task EnvironmentRequiresACompletePairAndIsOnlyFallback()
    {
        var missingPair = CreateManager(new MemoryCredentialStore(), environment: new Dictionary<string, string?>
        {
            [GoogleOAuthConfiguration.ClientIdEnvironmentVariable] = ClientId
        });
        await missingPair.InitializeAsync(CancellationToken.None);
        Assert.Equal(GoogleOAuthConfigurationStatus.Invalid, missingPair.Current.Status);

        var completePair = CreateManager(new MemoryCredentialStore(), environment: new Dictionary<string, string?>
        {
            [GoogleOAuthConfiguration.ClientIdEnvironmentVariable] = $" {ClientId} ",
            [GoogleOAuthConfiguration.ClientSecretEnvironmentVariable] = $" {ClientSecret} "
        });
        await completePair.InitializeAsync(CancellationToken.None);
        Assert.Equal(GoogleOAuthConfigurationStatus.Ready, completePair.Current.Status);
        Assert.Equal("Cấu hình môi trường phát triển", completePair.Current.SourceLabel);
        Assert.False(completePair.Current.CanRemoveImportedConfiguration);
    }

    [Fact]
    public async Task RemovingImportActivatesEnvironmentFallback()
    {
        var environment = new Dictionary<string, string?>
        {
            [GoogleOAuthConfiguration.ClientIdEnvironmentVariable] = "environment.apps.googleusercontent.com",
            [GoogleOAuthConfiguration.ClientSecretEnvironmentVariable] = "environment-secret"
        };
        var manager = CreateManager(new MemoryCredentialStore(), environment: environment);
        await manager.InitializeAsync(CancellationToken.None);
        await manager.ImportAsync(await manager.ValidateImportAsync("download.json", CancellationToken.None), CancellationToken.None);

        await manager.RemoveImportedAsync(CancellationToken.None);

        Assert.Equal(GoogleOAuthConfigurationStatus.Ready, manager.Current.Status);
        Assert.Equal("Cấu hình môi trường phát triển", manager.Current.SourceLabel);
    }

    [Fact]
    public async Task ProductionReaderDoesNotModifyOriginalFile()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CloudKeeperSN-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "client.json");
        var original = Encoding.UTF8.GetBytes(ValidJson());
        await File.WriteAllBytesAsync(path, original);
        try
        {
            var reader = new GoogleOAuthImportFileReader();
            var read = await reader.ReadAsync(path, GoogleDesktopOAuthJsonParser.MaximumFileBytes, CancellationToken.None);

            Assert.Equal(original, read);
            Assert.Equal(original, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    [Fact]
    public async Task ProductionReaderRejectsOversizedFileBeforeLoadingIt()
    {
        var directory = Path.Combine(Path.GetTempPath(), "CloudKeeperSN-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "oversized.json");
        await using (var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            stream.SetLength(GoogleDesktopOAuthJsonParser.MaximumFileBytes + 1);
        try
        {
            var reader = new GoogleOAuthImportFileReader();
            var exception = await Assert.ThrowsAsync<GoogleOAuthImportException>(() =>
                reader.ReadAsync(path, GoogleDesktopOAuthJsonParser.MaximumFileBytes, CancellationToken.None));
            Assert.Equal(GoogleOAuthImportError.FileTooLarge, exception.Error);
        }
        finally
        {
            File.Delete(path);
            Directory.Delete(directory);
        }
    }

    private static GoogleOAuthImportCandidate Parse(string json) =>
        new GoogleDesktopOAuthJsonParser().Parse(Encoding.UTF8.GetBytes(json));

    private static string ValidJson(
        string? clientId = ClientId,
        string? secret = ClientSecret,
        string authUri = "https://accounts.google.com/o/oauth2/auth",
        string tokenUri = "https://oauth2.googleapis.com/token",
        string redirect = "http://localhost",
        string extra = "") =>
        $$"""
        {
          "installed": {
            "client_id": {{JsonValue(clientId)}},
            "client_secret": {{JsonValue(secret)}},
            "auth_uri": "{{authUri}}",
            "token_uri": "{{tokenUri}}",
            "redirect_uris": ["{{redirect}}"]{{extra}}
          }
        }
        """;

    private static string JsonValue(string? value) => value is null ? "null" : $"\"{value}\"";

    private static GoogleOAuthConfigurationManager CreateManager(
        MemoryCredentialStore store,
        MemoryDiagnostics? diagnostics = null,
        Dictionary<string, string?>? environment = null,
        string? fileContent = "__default__") => new(
            store,
            new FakeEnvironment(environment ?? []),
            new FakeFileReader(fileContent == "__default__" ? ValidJson() : fileContent),
            new FixedClock(new DateTimeOffset(2026, 8, 9, 10, 30, 0, TimeSpan.Zero)),
            diagnostics ?? new MemoryDiagnostics());

    private sealed class FakeEnvironment(Dictionary<string, string?> values) : IGoogleOAuthEnvironment
    {
        public string? GetValue(string variableName) => values.GetValueOrDefault(variableName);
    }

    private sealed class FakeFileReader(string? content) : IGoogleOAuthImportFileReader
    {
        public Task<byte[]> ReadAsync(string path, long maximumBytes, CancellationToken cancellationToken) =>
            content is null
                ? Task.FromException<byte[]>(new FileNotFoundException())
                : Task.FromResult(Encoding.UTF8.GetBytes(content));
    }

    private sealed record FixedClock(DateTimeOffset UtcNow) : IGoogleOAuthClock;

    private sealed class MemoryCredentialStore : IProtectedCredentialStore
    {
        private readonly Dictionary<string, byte[]> _values = new(StringComparer.Ordinal);
        public byte[]? Stored
        {
            get => _values.GetValueOrDefault(Key("google-oauth-config", "desktop-client-v1"));
            set
            {
                if (value is null) _values.Remove(Key("google-oauth-config", "desktop-client-v1"));
                else _values[Key("google-oauth-config", "desktop-client-v1")] = value;
            }
        }
        public bool FailStore { get; set; }
        public Task<byte[]?> GetAsync(string providerId, string key, CancellationToken cancellationToken) =>
            Task.FromResult(_values.TryGetValue(Key(providerId, key), out var value) ? value.ToArray() : null);
        public Task StoreAsync(string providerId, string key, ReadOnlyMemory<byte> value, CancellationToken cancellationToken)
        {
            if (FailStore) throw new ProtectedCredentialException("test failure");
            _values[Key(providerId, key)] = value.ToArray();
            return Task.CompletedTask;
        }
        public Task DeleteAsync(string providerId, string key, CancellationToken cancellationToken) { _values.Remove(Key(providerId, key)); return Task.CompletedTask; }
        public Task ClearProviderAsync(string providerId, CancellationToken cancellationToken)
        {
            foreach (var key in _values.Keys.Where(key => key.StartsWith(providerId + "|", StringComparison.Ordinal)).ToArray())
                _values.Remove(key);
            return Task.CompletedTask;
        }
        public bool HasProvider(string providerId) => _values.Keys.Any(key => key.StartsWith(providerId + "|", StringComparison.Ordinal));
        public string Snapshot() => Stored is null ? "none" : Convert.ToBase64String(Stored);
        private static string Key(string providerId, string key) => providerId + "|" + key;
    }

    private sealed class MemoryDiagnostics : IProviderDiagnostics
    {
        public List<string> Events { get; } = [];
        public Task WriteAsync(string eventType, string vietnameseMessage, string? technicalDetails, CancellationToken cancellationToken)
        {
            Events.Add($"{eventType}|{vietnameseMessage}|{technicalDetails}");
            return Task.CompletedTask;
        }
    }
}
