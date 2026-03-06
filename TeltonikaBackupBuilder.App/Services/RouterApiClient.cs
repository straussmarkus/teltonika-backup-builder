using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TeltonikaBackupBuilder.App.Services;

public sealed class RouterApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;

    public RouterApiClient(string baseAddress)
    {
        _baseUrl = NormalizeBaseAddress(baseAddress);
        var handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (_, _, _, _) => true
        };

        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public void Dispose() => _httpClient.Dispose();

    public string BaseUrl => _baseUrl;

    public async Task<string> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var directPayload = JsonSerializer.Serialize(new { username, password });
        var directResult = await SendRawAsync(HttpMethod.Post, "/api/login", directPayload, token: null, cancellationToken);
        if (TryExtractToken(directResult.ResponseText, out var directToken))
        {
            return directToken;
        }

        var wrappedPayload = JsonSerializer.Serialize(new
        {
            data = new
            {
                username,
                password
            }
        });
        var wrappedResult = await SendRawAsync(HttpMethod.Post, "/api/login", wrappedPayload, token: null, cancellationToken);
        if (TryExtractToken(wrappedResult.ResponseText, out var wrappedToken))
        {
            return wrappedToken;
        }

        var error = directResult.ErrorMessage ?? wrappedResult.ErrorMessage ?? "Login fehlgeschlagen. Kein Token in der Antwort gefunden.";
        throw new InvalidOperationException(error);
    }

    public Task<ApiCallExecutionResult> ExecuteCallAsync(
        string method,
        string path,
        string? requestBody,
        string token,
        CancellationToken cancellationToken)
    {
        var httpMethod = new HttpMethod(method);
        return SendRawAsync(httpMethod, path, requestBody, token, cancellationToken);
    }

    public async Task<RouterDeviceInfo> GetDeviceInfoAsync(string token, CancellationToken cancellationToken)
    {
        var result = await SendRawAsync(HttpMethod.Get, "/api/system/device/status", requestBody: null, token, cancellationToken);
        if (!result.IsSuccess)
        {
            return new RouterDeviceInfo(null, null, result.ResponseText);
        }

        using var document = JsonDocument.Parse(result.ResponseText);
        var model = TryGetString(document.RootElement, "data.model")
            ?? TryGetString(document.RootElement, "data.device")
            ?? TryGetString(document.RootElement, "model");
        var firmware = TryGetString(document.RootElement, "data.firmware")
            ?? TryGetString(document.RootElement, "data.firmware_version")
            ?? TryGetString(document.RootElement, "firmware");

        return new RouterDeviceInfo(model, firmware, result.ResponseText);
    }

    public static bool TryExtractCliOutput(string responseText, out string output)
    {
        output = string.Empty;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            output = TryGetString(document.RootElement, "data.stdout")
                ?? TryGetString(document.RootElement, "data.output")
                ?? TryGetString(document.RootElement, "data.response")
                ?? TryGetString(document.RootElement, "stdout")
                ?? TryGetString(document.RootElement, "output")
                ?? string.Empty;
            return !string.IsNullOrWhiteSpace(output);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task<ApiCallExecutionResult> SendRawAsync(
        HttpMethod method,
        string path,
        string? requestBody,
        string? token,
        CancellationToken cancellationToken)
    {
        var url = BuildAbsoluteUrl(path);
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        if (requestBody != null)
        {
            request.Content = new StringContent(requestBody, Encoding.UTF8, "application/json");
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            var statusCode = (int)response.StatusCode;

            var apiSuccess = response.IsSuccessStatusCode && !IsExplicitApiFailure(text);
            var errorMessage = apiSuccess ? null : BuildErrorMessage(response.ReasonPhrase, text);
            return new ApiCallExecutionResult(apiSuccess, statusCode, text, errorMessage);
        }
        catch (TaskCanceledException ex)
        {
            return new ApiCallExecutionResult(false, null, string.Empty, $"Timeout: {ex.Message}");
        }
        catch (Exception ex)
        {
            return new ApiCallExecutionResult(false, null, string.Empty, ex.Message);
        }
    }

    private string BuildAbsoluteUrl(string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.ToString();
        }

        var normalizedPath = path.StartsWith("/", StringComparison.Ordinal) ? path : "/" + path;
        return _baseUrl + normalizedPath;
    }

    private static string NormalizeBaseAddress(string baseAddress)
    {
        var trimmed = baseAddress.Trim();
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            throw new InvalidOperationException("Router-Adresse fehlt.");
        }

        if (!trimmed.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            trimmed = "https://" + trimmed;
        }

        return trimmed.TrimEnd('/');
    }

    private static bool TryExtractToken(string responseText, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            token = TryGetString(document.RootElement, "token")
                ?? TryGetString(document.RootElement, "data.token")
                ?? TryGetString(document.RootElement, "jwt")
                ?? TryGetString(document.RootElement, "data.jwt")
                ?? string.Empty;
            return !string.IsNullOrWhiteSpace(token);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsExplicitApiFailure(string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            if (document.RootElement.TryGetProperty("success", out var successElement)
                && successElement.ValueKind == JsonValueKind.False)
            {
                return true;
            }

            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildErrorMessage(string? reasonPhrase, string responseText)
    {
        if (string.IsNullOrWhiteSpace(responseText))
        {
            return reasonPhrase ?? "HTTP-Fehler";
        }

        try
        {
            using var document = JsonDocument.Parse(responseText);
            var message = TryGetString(document.RootElement, "error.message")
                ?? TryGetString(document.RootElement, "message")
                ?? TryGetString(document.RootElement, "errors.0.message");
            if (!string.IsNullOrWhiteSpace(message))
            {
                return message;
            }
        }
        catch (JsonException)
        {
            // ignore JSON parsing errors, return raw text fallback below
        }

        return responseText.Length <= 300
            ? responseText
            : responseText[..300] + "...";
    }

    private static string? TryGetString(JsonElement root, string path)
    {
        var current = root;
        var parts = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            if (current.ValueKind == JsonValueKind.Array && int.TryParse(part, out var index))
            {
                if (index < 0 || index >= current.GetArrayLength())
                {
                    return null;
                }

                current = current[index];
                continue;
            }

            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(part, out var child))
            {
                return null;
            }

            current = child;
        }

        return current.ValueKind == JsonValueKind.String ? current.GetString() : current.ToString();
    }
}
