using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace APILinguagem.Services;

public sealed class TurnstileVerifier(
    HttpClient httpClient,
    IConfiguration configuration,
    ILogger<TurnstileVerifier> logger)
{
    private static readonly Uri VerifyUri = new("https://challenges.cloudflare.com/turnstile/v0/siteverify");

    public async Task<bool> VerifyAsync(
        string token,
        string expectedAction,
        string? remoteIp,
        CancellationToken cancellationToken)
    {
        var secret = configuration["CloudflareTurnstile:SecretKey"];
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(token))
        {
            logger.LogError("Cloudflare Turnstile secret or response token is missing.");
            return false;
        }

        var formValues = new Dictionary<string, string>
        {
            ["secret"] = secret,
            ["response"] = token
        };

        if (!string.IsNullOrWhiteSpace(remoteIp))
        {
            formValues["remoteip"] = remoteIp;
        }

        try
        {
            using var content = new FormUrlEncodedContent(formValues);
            using var response = await httpClient.PostAsync(VerifyUri, content, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cloudflare Siteverify returned HTTP {StatusCode}.", (int)response.StatusCode);
                return false;
            }

            var result = await response.Content.ReadFromJsonAsync<SiteVerifyResponse>(cancellationToken);
            return result is { Success: true }
                && string.Equals(result.Action, expectedAction, StringComparison.Ordinal);
        }
        catch (HttpRequestException exception)
        {
            logger.LogError(exception, "Could not reach Cloudflare Siteverify.");
            return false;
        }
        catch (JsonException exception)
        {
            logger.LogError(exception, "Cloudflare Siteverify returned an invalid response.");
            return false;
        }
    }

    private sealed class SiteVerifyResponse
    {
        [JsonPropertyName("success")]
        public bool Success { get; init; }

        [JsonPropertyName("action")]
        public string? Action { get; init; }
    }
}
