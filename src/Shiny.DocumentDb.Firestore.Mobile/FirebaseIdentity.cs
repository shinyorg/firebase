using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Shiny.DocumentDb.Firestore.Mobile;

/// <summary>A signed-in Firebase user (managed identity), with the tokens needed for authenticated calls.</summary>
public sealed record FirebaseUser(
    string Uid,
    string IdToken,
    string RefreshToken,
    DateTimeOffset ExpiresAt,
    bool IsAnonymous,
    string? Email);

/// <summary>
/// Managed Firebase identity — signs users in via the Firebase Auth REST API (no native binding), exposes the
/// current user's <c>uid</c> (what Firestore security rules see as <c>request.auth.uid</c>) and a fresh ID
/// token, and raises <see cref="AuthStateChanged"/> on sign-in/out.
/// </summary>
/// <remarks>
/// This obtains and refreshes the user's token in managed code. Wiring the token into the <b>native</b>
/// Firestore SDK's request auth (so rules are enforced on native reads/writes) is the remaining integration
/// seam — it needs a native <c>FirebaseAuth.signInWithCustomToken</c>, deferred with the native auth binding.
/// Until then, scope per-user data by collection path (e.g. <c>MapTypeToCollection&lt;T&gt;($"users/{uid}/…")</c>).
/// </remarks>
public interface IFirebaseIdentity
{
    FirebaseUser? CurrentUser { get; }
    string? CurrentUserId => this.CurrentUser?.Uid;

    Task<FirebaseUser> SignInAnonymouslyAsync(CancellationToken cancellationToken = default);
    Task<FirebaseUser> SignInWithEmailPasswordAsync(string email, string password, CancellationToken cancellationToken = default);
    Task<FirebaseUser> SignUpWithEmailPasswordAsync(string email, string password, CancellationToken cancellationToken = default);

    /// <summary>Returns a valid ID token for the current user, refreshing it if it is within a minute of expiry.</summary>
    Task<string> GetIdTokenAsync(CancellationToken cancellationToken = default);

    void SignOut();

    event EventHandler<FirebaseUser?>? AuthStateChanged;
}

/// <summary>Options for <see cref="IFirebaseIdentity"/>. The API key is the Firebase Web API key.</summary>
public sealed class FirebaseIdentityOptions
{
    public string ApiKey { get; set; } = "";

    /// <summary>Optional <c>host:port</c> of the Firebase Auth emulator; when set, REST calls target it instead of production.</summary>
    public string? AuthEmulatorHost { get; set; }
}

/// <summary>Firebase Auth REST (identitytoolkit + securetoken) implementation of <see cref="IFirebaseIdentity"/>.</summary>
public sealed class FirebaseRestIdentity : IFirebaseIdentity
{
    readonly FirebaseIdentityOptions options;
    readonly HttpClient http;
    readonly string identityBase;
    readonly string tokenBase;
    FirebaseUser? current;

    public FirebaseRestIdentity(FirebaseIdentityOptions options, HttpClient? httpClient = null)
    {
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            throw new ArgumentException("FirebaseIdentityOptions.ApiKey is required.", nameof(options));
        this.http = httpClient ?? new HttpClient();

        // The Auth emulator proxies both Google endpoints under its own host.
        this.identityBase = options.AuthEmulatorHost is { Length: > 0 } h
            ? $"http://{h}/identitytoolkit.googleapis.com/v1"
            : "https://identitytoolkit.googleapis.com/v1";
        this.tokenBase = options.AuthEmulatorHost is { Length: > 0 } h2
            ? $"http://{h2}/securetoken.googleapis.com/v1"
            : "https://securetoken.googleapis.com/v1";
    }

    public FirebaseUser? CurrentUser => this.current;

    public event EventHandler<FirebaseUser?>? AuthStateChanged;

    public Task<FirebaseUser> SignInAnonymouslyAsync(CancellationToken cancellationToken = default)
        => this.PostSignIn("accounts:signUp", new SignInRequest(), isAnonymous: true, null, cancellationToken);

    public Task<FirebaseUser> SignInWithEmailPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
        => this.PostSignIn("accounts:signInWithPassword", new SignInRequest { Email = email, Password = password }, isAnonymous: false, email, cancellationToken);

    public Task<FirebaseUser> SignUpWithEmailPasswordAsync(string email, string password, CancellationToken cancellationToken = default)
        => this.PostSignIn("accounts:signUp", new SignInRequest { Email = email, Password = password }, isAnonymous: false, email, cancellationToken);

    async Task<FirebaseUser> PostSignIn(string method, SignInRequest body, bool isAnonymous, string? email, CancellationToken ct)
    {
        var url = $"{this.identityBase}/{method}?key={this.options.ApiKey}";
        var resp = await this.http
            .PostAsJsonAsync(url, body, FirebaseAuthJsonContext.Default.SignInRequest, ct)
            .ConfigureAwait(false);
        await EnsureOk(resp, ct).ConfigureAwait(false);
        var r = (await resp.Content
            .ReadFromJsonAsync(FirebaseAuthJsonContext.Default.SignInResponse, ct)
            .ConfigureAwait(false))!;

        var user = new FirebaseUser(
            r.LocalId!, r.IdToken!, r.RefreshToken!,
            DateTimeOffset.UtcNow.AddSeconds(ParseInt(r.ExpiresIn, 3600)),
            isAnonymous, email);
        this.SetCurrent(user);
        return user;
    }

    public async Task<string> GetIdTokenAsync(CancellationToken cancellationToken = default)
    {
        var user = this.current ?? throw new InvalidOperationException("No signed-in Firebase user.");
        if (DateTimeOffset.UtcNow < user.ExpiresAt.AddMinutes(-1))
            return user.IdToken;

        // Refresh via the secure-token endpoint.
        var url = $"{this.tokenBase}/token?key={this.options.ApiKey}";
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "refresh_token",
            ["refresh_token"] = user.RefreshToken
        });
        var resp = await this.http.PostAsync(url, form, cancellationToken).ConfigureAwait(false);
        await EnsureOk(resp, cancellationToken).ConfigureAwait(false);
        var r = (await resp.Content
            .ReadFromJsonAsync(FirebaseAuthJsonContext.Default.RefreshResponse, cancellationToken)
            .ConfigureAwait(false))!;

        var refreshed = user with
        {
            IdToken = r.IdToken!,
            RefreshToken = r.RefreshToken ?? user.RefreshToken,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(ParseInt(r.ExpiresIn, 3600))
        };
        this.SetCurrent(refreshed);
        return refreshed.IdToken;
    }

    public void SignOut() => this.SetCurrent(null);

    void SetCurrent(FirebaseUser? user)
    {
        this.current = user;
        this.AuthStateChanged?.Invoke(this, user);
    }

    static async Task EnsureOk(HttpResponseMessage resp, CancellationToken ct)
    {
        if (resp.IsSuccessStatusCode)
            return;
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        throw new InvalidOperationException($"Firebase Auth request failed ({(int)resp.StatusCode}): {body}");
    }

    static int ParseInt(string? s, int fallback) => int.TryParse(s, out var v) ? v : fallback;
}

// The Auth REST payloads. Top-level (not nested) and paired with the source-generated context below so the
// identity client carries no reflection-based serialization — it is the one part of this provider that
// UseReflectionFallback cannot switch off, since it has no JsonTypeInfo<T> parameter to thread through.
sealed class SignInRequest
{
    /// <summary>Omitted for anonymous sign-in, which posts only <c>returnSecureToken</c>.</summary>
    [JsonPropertyName("email")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Email { get; set; }

    [JsonPropertyName("password")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Password { get; set; }

    [JsonPropertyName("returnSecureToken")] public bool ReturnSecureToken { get; set; } = true;
}

sealed class SignInResponse
{
    [JsonPropertyName("localId")] public string? LocalId { get; set; }
    [JsonPropertyName("idToken")] public string? IdToken { get; set; }
    [JsonPropertyName("refreshToken")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expiresIn")] public string? ExpiresIn { get; set; }
}

sealed class RefreshResponse
{
    [JsonPropertyName("id_token")] public string? IdToken { get; set; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")] public string? ExpiresIn { get; set; }
}

[JsonSerializable(typeof(SignInRequest))]
[JsonSerializable(typeof(SignInResponse))]
[JsonSerializable(typeof(RefreshResponse))]
sealed partial class FirebaseAuthJsonContext : JsonSerializerContext;
