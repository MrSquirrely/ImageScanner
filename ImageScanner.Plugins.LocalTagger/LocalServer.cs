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

    /// <summary>How long to wait for one reply before giving up (generation on a CPU can be slow).</summary>
    public TimeSpan ResponseTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Show KoboldCpp's console when we start it. Off by default: clicking into a console pauses the program in it.</summary>
    public bool ShowConsole { get; init; }

    /// <summary>Where KoboldCpp's output goes when its console is hidden.</summary>
    public string? LogPath { get; init; }
}

/// <summary>
/// Talks to a local OpenAI-compatible vision server (KoboldCpp, llama.cpp server, LM Studio, Ollama)
/// and, when a KoboldCpp executable is configured, starts it on demand and stops it on exit.
/// </summary>
public sealed class LocalServer : IDisposable {
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromMinutes(5);

    // Per-request deadlines come from LocalServerOptions.ResponseTimeout, so the client itself never times out
    private readonly HttpClient http = new() { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Lock logLock = new();
    private StreamWriter? log;
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
                startedProcess = StartKoboldCpp(options);
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
            using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using HttpRequestMessage request = CreateRequest(HttpMethod.Get, options, "v1/models");
            using HttpResponseMessage response = await http.SendAsync(request, timeout.Token);
            JsonNode? body = await response.Content.ReadFromJsonAsync<JsonNode>(timeout.Token);
            return body?["data"]?[0]?["id"]?.GetValue<string>();
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException or InvalidOperationException
                                              or OperationCanceledException && !cancellationToken.IsCancellationRequested) {
            return null;
        }
    }

    /// <summary>
    /// Sends a chat completion request and returns the assistant's text. A dropped connection is retried
    /// once after checking the server is still up; a reply that takes longer than the response timeout
    /// stops the batch with an explanation.
    /// </summary>
    public async Task<string?> ChatAsync(LocalServerOptions options, JsonObject payload, CancellationToken cancellationToken) {
        for (int attempt = 1; ; attempt++) {
            try {
                return await SendChatAsync(options, payload, cancellationToken);
            }
            catch (HttpRequestException exception) when (attempt == 1) {
                // Local servers often close idle connections or restart; give it a moment and try again
                Debug.WriteLine($"Local AI request failed, retrying: {exception.Message}");
                lastHealthy = DateTime.MinValue;
                await Task.Delay(TimeSpan.FromSeconds(3), cancellationToken);
                await EnsureRunningAsync(options, cancellationToken);
            }
            catch (HttpRequestException exception) {
                lastHealthy = DateTime.MinValue;
                throw new TagProviderException(
                    $"Lost connection to the local AI server at {options.BaseUrl}. If KoboldCpp is still running, check its window: " +
                    "clicking inside a console window pauses the program (the title shows \"Select\"); press Esc there to resume.",
                    isConfigurationError: true, exception);
            }
        }
    }

    private async Task<string?> SendChatAsync(LocalServerOptions options, JsonObject payload, CancellationToken cancellationToken) {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, options, "v1/chat/completions");
        request.Content = JsonContent.Create(payload);

        // A fresh connection per request: reusing one the server already closed is a common cause of failed requests
        request.Headers.ConnectionClose = true;

        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(options.ResponseTimeout);
        try {
            using HttpResponseMessage response = await http.SendAsync(request, deadline.Token);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) {
                throw new TagProviderException("The local AI server rejected the password. Check it in Settings → Plugins → Local AI tagging.", isConfigurationError: true);
            }

            if (!response.IsSuccessStatusCode) {
                string detail = await response.Content.ReadAsStringAsync(deadline.Token);
                throw new TagProviderException($"The local AI server returned {(int)response.StatusCode}: {Truncate(detail, 200)}");
            }

            try {
                JsonNode? body = await response.Content.ReadFromJsonAsync<JsonNode>(deadline.Token);
                JsonNode? choice = body?["choices"]?[0];
                return choice?["message"]?["content"]?.GetValue<string>() ?? choice?["text"]?.GetValue<string>();
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException) {
                throw new TagProviderException("The local AI server sent a response that isn't valid JSON.", innerException: exception);
            }
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested) {
            lastHealthy = DateTime.MinValue;
            throw new TagProviderException(
                $"The local AI server didn't answer within {options.ResponseTimeout.TotalSeconds:0} seconds. If its console window's title shows " +
                "\"Select\", it's paused: click into it and press Esc. On a slow (CPU-only) PC, raise the response timeout in the plugin's settings.",
                isConfigurationError: true, exception);
        }
    }

    private Process? StartKoboldCpp(LocalServerOptions options) {
        ProcessStartInfo start = new(options.KoboldCppPath!, BuildArguments(options)) {
            WorkingDirectory = Path.GetDirectoryName(options.KoboldCppPath)!
        };

        if (options.ShowConsole || options.LogPath == null) {
            start.UseShellExecute = true;
            start.WindowStyle = ProcessWindowStyle.Minimized;
            return Process.Start(start);
        }

        // No console: nothing for a stray click to pause. Output is kept in a log for troubleshooting.
        start.UseShellExecute = false;
        start.CreateNoWindow = true;
        start.RedirectStandardOutput = true;
        start.RedirectStandardError = true;

        lock (logLock) {
            log?.Dispose();
            log = new StreamWriter(options.LogPath, append: false) { AutoFlush = true };
            log.WriteLine($"[{DateTime.Now:u}] {start.FileName} {start.Arguments}");
        }

        Process? process = Process.Start(start);
        if (process != null) {
            // Both streams must be drained, or KoboldCpp blocks once the pipe buffer fills up
            process.OutputDataReceived += (_, e) => WriteLog(e.Data);
            process.ErrorDataReceived += (_, e) => WriteLog(e.Data);
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        return process;
    }

    private void WriteLog(string? line) {
        if (line == null) {
            return;
        }

        lock (logLock) {
            log?.WriteLine(line);
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
        lock (logLock) {
            log?.Dispose();
            log = null;
        }
    }
}
