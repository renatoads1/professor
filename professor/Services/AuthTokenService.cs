using System.Net.Http.Headers;
using Microsoft.JSInterop;

namespace professor.Services;

public sealed class AuthTokenService(IJSRuntime jsRuntime, HttpClient httpClient) : IAsyncDisposable
{
    private const string StorageModulePath = "./authStorage.js";
    private IJSObjectReference? _module;

    public bool IsAuthenticated { get; private set; }
    public string? UserName { get; private set; }

    public async Task SetTokenAsync(string token, string userName)
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("setToken", token, userName);
        UserName = userName;
        SetAuthorizationHeader(token);
    }

    public async Task RestoreAsync()
    {
        var module = await GetModuleAsync();
        var token = await module.InvokeAsync<string?>("getToken");
        UserName = await module.InvokeAsync<string?>("getUserName");
        SetAuthorizationHeader(token);
    }

    public async Task ClearAsync()
    {
        var module = await GetModuleAsync();
        await module.InvokeVoidAsync("clearToken");
        UserName = null;
        SetAuthorizationHeader(null);
    }

    private async Task<IJSObjectReference> GetModuleAsync()
    {
        _module ??= await jsRuntime.InvokeAsync<IJSObjectReference>("import", StorageModulePath);
        return _module;
    }

    private void SetAuthorizationHeader(string? token)
    {
        IsAuthenticated = !string.IsNullOrWhiteSpace(token);
        httpClient.DefaultRequestHeaders.Authorization = IsAuthenticated
            ? new AuthenticationHeaderValue("Bearer", token)
            : null;
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            await _module.DisposeAsync();
        }
    }
}
