using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.JSInterop;

namespace professor.Services;

public sealed class AuthTokenService(IJSRuntime jsRuntime, HttpClient httpClient) : IAsyncDisposable
{
    private const string StorageModulePath = "./authStorage.js";
    private IJSObjectReference? _module;

    public bool IsAuthenticated { get; private set; }
    public bool IsManager { get; private set; }
    public bool IsStudent { get; private set; }
    public bool IsTeacher { get; private set; }
    public string? UserName { get; private set; }
    public string? AccessToken { get; private set; }
    public event Action? AuthenticationStateChanged;

    public async Task SetTokenAsync(string token, string userName)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("setToken", token, userName);
        UserName = userName;
        SetAuthorizationHeader(token);
        AuthenticationStateChanged?.Invoke();
    }

    public async Task RestoreAsync()
    {
        var module = await GetModuleAsync();
        var token = await module.InvokeAsync<string?>("getToken");
        UserName = await module.InvokeAsync<string?>("getUserName");
        SetAuthorizationHeader(token);
        AuthenticationStateChanged?.Invoke();
    }

    public async Task ClearAsync()
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("clearToken");
        UserName = null;
        SetAuthorizationHeader(null);
        AuthenticationStateChanged?.Invoke();
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", StorageModulePath);
        return _module;
    }

    private void SetAuthorizationHeader(string? token)
    {
        AccessToken = string.IsNullOrWhiteSpace(token) ? null : token;
        IsAuthenticated = AccessToken is not null;
        IsManager = AccessToken is not null && LerRoles(AccessToken).Contains("Gerente", StringComparer.Ordinal);
        var tipoPerfil = AccessToken is null ? null : LerTipoPerfil(AccessToken);
        IsStudent = string.Equals(tipoPerfil, "aluno", StringComparison.OrdinalIgnoreCase);
        IsTeacher = string.Equals(tipoPerfil, "professor", StringComparison.OrdinalIgnoreCase);
        httpClient.DefaultRequestHeaders.Authorization = IsAuthenticated
            ? new AuthenticationHeaderValue("Bearer", AccessToken)
            : null;
    }

    private static string? LerTipoPerfil(string token)
    {
        var segmentos = token.Split('.');
        if (segmentos.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = segmentos[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            return json.RootElement.TryGetProperty("tipoPerfil", out var propriedade)
                && propriedade.ValueKind == JsonValueKind.String
                    ? propriedade.GetString()
                    : null;
        }
        catch (FormatException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static IReadOnlyCollection<string> LerRoles(string token)
    {
        var segmentos = token.Split('.');
        if (segmentos.Length != 3)
        {
            return [];
        }

        try
        {
            var payload = segmentos[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
            using var json = JsonDocument.Parse(Convert.FromBase64String(payload));
            var claims = json.RootElement.EnumerateObject()
                .Where(property => property.Name is ClaimTypes.Role or "role" or "roles")
                .SelectMany(property => property.Value.ValueKind == JsonValueKind.Array
                    ? property.Value.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!)
                    : property.Value.ValueKind == JsonValueKind.String ? [property.Value.GetString()!] : []);
            return claims.ToArray();
        }
        catch (FormatException)
        {
            return [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}
