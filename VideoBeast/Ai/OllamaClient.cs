using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace VideoBeast.Ai;

public sealed class OllamaClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private string _baseUrl;

    public OllamaClient(string baseUrl)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl);
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
    }

    public void UpdateBaseUrl(string baseUrl)
    {
        _baseUrl = NormalizeBaseUrl(baseUrl);
    }

    private static string NormalizeBaseUrl(string baseUrl)
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            return "http://localhost:11434";

        var normalized = baseUrl.Trim();
        if (normalized.EndsWith("/"))
            normalized = normalized.TrimEnd('/');

        return normalized;
    }

    public async Task<bool> IsAvailableAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUrl}/api/tags";
            var response = await _httpClient.GetAsync(url, ct);
            
            if (!response.IsSuccessStatusCode)
                return false;

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            return doc.RootElement.TryGetProperty("models", out _);
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<string>> GetModelsAsync(CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUrl}/api/tags";
            var response = await _httpClient.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(json);

            var models = new HashSet<string>(StringComparer.Ordinal);
            
            if (doc.RootElement.TryGetProperty("models", out var modelsArray) && modelsArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var model in modelsArray.EnumerateArray())
                {
                    if (model.TryGetProperty("name", out var name))
                    {
                        var modelName = name.GetString();
                        if (!string.IsNullOrWhiteSpace(modelName))
                        {
                            models.Add(modelName);
                        }
                    }
                }
            }

            return models.OrderBy(m => m, StringComparer.Ordinal).ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public async Task<string> ChatAsync(string model, string systemPrompt, string userMessage, CancellationToken ct = default)
    {
        try
        {
            var url = $"{_baseUrl}/api/chat";
            var requestBody = new
            {
                model = model,
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = userMessage }
                },
                stream = false
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(url, content, ct);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync();
            var doc = JsonDocument.Parse(responseJson);

            if (doc.RootElement.TryGetProperty("message", out var message))
            {
                if (message.TryGetProperty("content", out var messageContent))
                {
                    return messageContent.GetString() ?? string.Empty;
                }
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Ollama request failed: {ex.Message}", ex);
        }
    }

    public void Dispose()
    {
        _httpClient?.Dispose();
    }
}
