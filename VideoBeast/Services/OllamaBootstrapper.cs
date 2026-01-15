using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VideoBeast.Ai;

namespace VideoBeast.Services;

public static class OllamaBootstrapper
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(250);

    public enum BootstrapStatus
    {
        Success,
        AlreadyRunning,
        NotLocalUrl,
        OllamaNotFound,
        ProcessStartFailed,
        Timeout,
        Cancelled
    }

    public sealed class BootstrapResult
    {
        public BootstrapStatus Status { get; init; }
        public string? Message { get; init; }
        public bool IsSuccess => Status is BootstrapStatus.Success or BootstrapStatus.AlreadyRunning;

        public static BootstrapResult Success() =>
            new() { Status = BootstrapStatus.Success, Message = "Ollama started successfully" };

        public static BootstrapResult AlreadyRunning() =>
            new() { Status = BootstrapStatus.AlreadyRunning, Message = "Ollama is already running" };

        public static BootstrapResult NotLocal(string baseUrl) =>
            new() { Status = BootstrapStatus.NotLocalUrl, Message = $"Cannot start Ollama for non-local URL: {baseUrl}" };

        public static BootstrapResult NotFound() =>
            new() { Status = BootstrapStatus.OllamaNotFound, Message = "Ollama executable not found in PATH" };

        public static BootstrapResult StartFailed(string errorMessage) =>
            new() { Status = BootstrapStatus.ProcessStartFailed, Message = $"Failed to start Ollama: {errorMessage}" };

        public static BootstrapResult Timeout() =>
            new() { Status = BootstrapStatus.Timeout, Message = "Timed out waiting for Ollama to become available" };

        public static BootstrapResult Cancelled() =>
            new() { Status = BootstrapStatus.Cancelled, Message = "Bootstrap operation was cancelled" };
    }

    public static async Task<BootstrapResult> TryStartAndWaitAsync(
        string baseUrl,
        OllamaClient ollamaClient,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (await ollamaClient.IsAvailableAsync(cancellationToken))
            {
                return BootstrapResult.AlreadyRunning();
            }

            if (!IsLocalUrl(baseUrl))
            {
                return BootstrapResult.NotLocal(baseUrl);
            }

            if (!TryStartOllamaProcess(out var errorMessage))
            {
                return errorMessage != null
                    ? BootstrapResult.StartFailed(errorMessage)
                    : BootstrapResult.NotFound();
            }

            var available = await PollForAvailabilityAsync(
                ollamaClient,
                DefaultTimeout,
                DefaultPollInterval,
                cancellationToken);

            return available
                ? BootstrapResult.Success()
                : BootstrapResult.Timeout();
        }
        catch (OperationCanceledException)
        {
            return BootstrapResult.Cancelled();
        }
    }

    public static bool IsLocalUrl(string normalizedUrl)
    {
        try
        {
            var uri = new Uri(normalizedUrl);
            var host = uri.Host.ToLowerInvariant();
            return host is "localhost" or "127.0.0.1" or "::1" or "[::1]";
        }
        catch
        {
            return false;
        }
    }

    private static bool TryStartOllamaProcess(out string? errorMessage)
    {
        errorMessage = null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "ollama",
                Arguments = "serve",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            var process = Process.Start(startInfo);
            if (process == null)
            {
                return false;
            }

            return true;
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 2)
        {
            return false;
        }
        catch (Exception ex)
        {
            errorMessage = ex.Message;
            return false;
        }
    }

    private static async Task<bool> PollForAvailabilityAsync(
        OllamaClient client,
        TimeSpan timeout,
        TimeSpan pollInterval,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await client.IsAvailableAsync(cancellationToken))
            {
                return true;
            }

            await Task.Delay(pollInterval, cancellationToken);
        }

        return false;
    }
}
