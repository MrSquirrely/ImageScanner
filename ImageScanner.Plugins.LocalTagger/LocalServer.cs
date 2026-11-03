using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ImageScanner.Plugins.LocalTagger;

public sealed class LocalServerOptions {
    public required Uri BaseUrl { get; init; }
    public string? ApiPassword { get; init; }
    public string? KoboldCppPath { get; init; }
    public string? ModelPath { get; init; }
    public string? MmprojPath { get; init; }
}

/// <summary>
/// Talks to a local OpenAI-compatible vision server (KoboldCpp, llama.cpp server, LM Studio, Ollama)
/// and, when a KoboldCpp executable is configured, starts it on demand and stops it on exit.
/// </summary>
public sealed class LocalServer : IDisposable {
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly SemaphoreSlim startLock = new(1, 1);
    private Process? startedProcess;
    private DateTime lastHealthy = DateTime.MinValue;

    public bool StartedByUs => startedProcess is { HasExited: false };

    /// <summary>
    /// Makes sure the server answers, starting KoboldCpp if configured. Throws a configuration
    /// <see cref="TagProviderException"/> when the server can't be reached.
    /// </summary>
    public async Task EnsureRunningAsync(LocalServerOptions options, CancellationToken cancellationToken) {
        if (DateTime.UtcNow - lastHealthy < TimeSpan.FromSeconds(30)) {
            return;
        }

        await startLock.WaitAsync(cancellationToken);
        try {
            if (await IsReachableAsync(options, cancellationToken)) {
                lastHealthy = DateTime.UtcNow;
                return;
            }

            if (string.IsNullOrWhiteSpace(options.KoboldCppPath)) {
                throw new TagProviderException(
                    $"No local AI server is answering at {options.BaseUrl}. Start KoboldCpp (or another OpenAI-compatible vision server), " +
                    "or set the KoboldCpp path in Settings → Plugins → Local AI tagging so it starts automatically.", isConfigurationError: true);
            }

            if (!File.Exists(options.KoboldCppPath)) {
                throw new TagProviderException($"KoboldCpp wasn't found at {options.KoboldCppPath}.", isConfigurationError: true);
            }

            if (startedProcess is not { HasExited: false }) {
                startedProcess = Process.Start(new ProcessStartInfo(options.KoboldCppPath, BuildArguments(options)) {
                    UseShellExecute = true,
                    WindowStyle = ProcessWindowStyle.Minimized,
                    WorkingDirectory = Path.GetDirectoryName(options.KoboldCppPath)!
                });
            }

            // Loading a vision model can take a while, especially the first time
            DateTime deadline = DateTime.UtcNow + StartupTimeout;
            while (DateTime.UtcNow < deadline) {
                if (startedProcess is { HasExited: true }) {
                    throw new TagProviderException("KoboldCpp closed while starting. Run it once by hand to check the model settings.", isConfigurationError: true);
                }

                if (await IsReachableAsync(options, cancellationToken)) {
                    lastHealthy = DateTime.UtcNow;
                    return;
                }

                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }

            throw new TagProviderException($"KoboldCpp didn't become ready within {StartupTimeout.TotalMinutes:0} minutes.", isConfigurationError: true);
        }
        finally {
            startLock.Release();
        }
    }

    public async Task<bool> IsReachableAsync(LocalServerOptions options, CancellationToken cancellationToken) {
        try {
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(3));
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, options, "v1/models");
            using HttpResponseMessage response = await http.SendAsync(request, timeout.Token);
            return response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested) {
            return false;
        }
    }

    /// <summary>
    /// The first model the server reports. LM Studio and Ollama need a model name in each request;
    /// KoboldCpp and llama.cpp ignore it.
    /// </summary>
    public async Task<string?> GetDefaultModelAsync(LocalServerOptions options, CancellationToken cancellationToken) {
        try {
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, options, "v1/models");
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            JsonNode? body = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
            return body?["data"]?[0]?["id"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException) {
            return null;
        }
    }

    /// <summary>
    /// Sends a chat completion request and returns the assistant's text.
    /// </summary>
    public async Task<string?> ChatAsync(LocalServerOptions options, JsonObject payload, CancellationToken cancellationToken) {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, options, "v1/chat/completions");
        request.Content = JsonContent.Create(payload);

        HttpResponseMessage response;
        try {
            response = await http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException exception) {
            lastHealthy = DateTime.MinValue;
            throw new TagProviderException($"Lost connection to the local AI server at {options.BaseUrl}.", isConfigurationError: true, exception);
        }

        using (response) {
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
                throw new TagProviderException("The local AI server rejected the password. Check it in Settings → Plugins → Local AI tagging.", isConfigurationError: true);
            }

            if (!response.IsSuccessStatusCode) {
                string detail = await response.Content.ReadAsStringAsync(cancellationToken);
                throw new TagProviderException($"The local AI server returned {(int)response.StatusCode}: {Truncate(detail, 200)}");
            }

            try {
                JsonNode? body = await response.Content.ReadFromJsonAsync<JsonNode>(cancellationToken);
                JsonNode? choice = body?["choices"]?[0];
                return choice?["message"]?["content"]?.GetValue<string>() ?? choice?["text"]?.GetValue<string>();
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException) {
                throw new TagProviderException("The local AI server sent a response that isn't valid JSON.", innerException: exception);
            }
        }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, LocalServerOptions options, string path) {
        HttpRequestMessage request = new(method, new Uri(options.BaseUrl, path));
        if (!string.IsNullOrWhiteSpace(options.ApiPassword)) {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiPassword);
        }

        return request;
    }

    private static string BuildArguments(LocalServerOptions options) {
        List<string> arguments = [];
        if (!string.IsNullOrWhiteSpace(options.ModelPath)) {
            // A .kcpps file holds a complete KoboldCpp launch configuration (model, mmproj, GPU layers ...)
            bool isConfig = options.ModelPath.EndsWith(".kcpps", StringComparison.OrdinalIgnoreCase);
            arguments.Add(isConfig ? "--config" : "--model");
            arguments.Add(Quote(options.ModelPath));
            if (!isConfig && !string.IsNullOrWhiteSpace(options.MmprojPath)) {
                arguments.Add("--mmproj");
                arguments.Add(Quote(options.MmprojPath));
            }
        }

        if (!options.BaseUrl.IsDefaultPort) {
            arguments.Add("--port");
            arguments.Add(options.BaseUrl.Port.ToString());
        }

        return string.Join(' ', arguments);
    }

    private static string Quote(string value) => $"\"{value.Trim().Trim('"')}\"";

    private static string Truncate(string text, int length) => text.Length <= length ? text : text[..length] + "…";

    /// <summary>
    /// Stops KoboldCpp if this plugin started it; a server the user started stays running.
    /// </summary>
    public void Dispose() {
        try {
            if (startedProcess is { HasExited: false }) {
                startedProcess.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) {
            // Already exited
        }

        startedProcess?.Dispose();
        http.Dispose();
    }
}
