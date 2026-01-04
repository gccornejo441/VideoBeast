using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

using Windows.Storage;

namespace VideoBeast;

public static class PlayerSettingsStore
{
    private const string Key = "player.settings.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static PlayerSettings Load()
    {
        try
        {
            var values = ApplicationData.Current.LocalSettings.Values;

            if (values.TryGetValue(Key,out var raw)
                && raw is string json
                && !string.IsNullOrWhiteSpace(json))
            {
                json = NormalizeJson(json);

                return JsonSerializer.Deserialize<PlayerSettings>(json,Options)
                       ?? new PlayerSettings();
            }
        }
        catch
        {
            // swallow + fall back
        }

        return new PlayerSettings();
    }

    public static void Save(PlayerSettings settings)
    {
        var json = JsonSerializer.Serialize(settings,Options);
        ApplicationData.Current.LocalSettings.Values[Key] = json;
    }

    public static string ExportJson(PlayerSettings settings)
        => JsonSerializer.Serialize(settings,Options);

    public static bool TryImportJson(string json,out PlayerSettings settings,out string error)
    {
        try
        {
            json = NormalizeJson(json);

            settings = JsonSerializer.Deserialize<PlayerSettings>(json,Options)
                       ?? new PlayerSettings();

            error = "";
            return true;
        }
        catch (Exception ex)
        {
            settings = new PlayerSettings();
            error = ex.Message;
            return false;
        }
    }

    // Back-compat: if older versions saved "PlayerStretch", map it to "Stretch".
    // This keeps old settings working without changing your PlayerSettings JSON shape.
    private static string NormalizeJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return json;

        if (json.Contains("\"PlayerStretch\"",StringComparison.Ordinal))
        {
            // Replace the property name only (robust vs whitespace)
            json = Regex.Replace(
                json,
                "\"PlayerStretch\"\\s*:",
                "\"Stretch\":",
                RegexOptions.CultureInvariant
            );
        }

        return json;
    }
}
