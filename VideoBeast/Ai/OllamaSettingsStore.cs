using System;
using System.Text.Json;
using Windows.Storage;

namespace VideoBeast.Ai;

public static class OllamaSettingsStore
{
    private const string SettingsKey = "ollama.settings.json";

    public static OllamaSettings Load()
    {
        try
        {
            var localSettings = ApplicationData.Current.LocalSettings;
            if (localSettings.Values.TryGetValue(SettingsKey, out var json) && json is string jsonStr)
            {
                var settings = JsonSerializer.Deserialize<OllamaSettings>(jsonStr);
                return settings ?? new OllamaSettings();
            }
        }
        catch
        {
        }

        return new OllamaSettings();
    }

    public static void Save(OllamaSettings settings)
    {
        try
        {
            var json = JsonSerializer.Serialize(settings);
            var localSettings = ApplicationData.Current.LocalSettings;
            localSettings.Values[SettingsKey] = json;
        }
        catch
        {
        }
    }
}
