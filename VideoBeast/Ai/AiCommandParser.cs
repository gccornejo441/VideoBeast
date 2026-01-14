using System;
using System.Text.Json;

namespace VideoBeast.Ai;

public sealed class AiCommandParser
{
    public sealed class ParsedCommand
    {
        public string Type { get; set; } = "unknown";
        public JsonElement Args { get; set; }
        public bool NeedsConfirmation { get; set; }
        public string? ConfirmationText { get; set; }
    }

    private const string SystemPrompt = @"You are a command parser for a video library app. Parse user commands into JSON.

COMMANDS:
1. open_page: { ""page"": ""player"" | ""playlists"" | ""settings"" }
2. play_video: { ""query"": string, ""scope"": ""currentFolder"" | ""library"" }
3. import: { ""source"": ""picker"" | ""clipboard"" | ""downloads"" }
4. create_playlist: { ""name"": string, ""fromScope"": ""currentFolder"" | ""library"", ""filter"": string? }

OUTPUT FORMAT (JSON only, no markdown):
{
  ""type"": ""open_page"" | ""play_video"" | ""import"" | ""create_playlist"" | ""unknown"",
  ""args"": { ... },
  ""needsConfirmation"": boolean,
  ""confirmationText"": string | null
}

RULES:
- Return ONLY valid JSON, no markdown, no extra text
- If command is unclear, use type ""unknown"" with helpful confirmationText
- Destructive actions (delete/rename) should return type ""unknown"" with explanation
- Be flexible with natural language (e.g., ""play video X"", ""open settings"", ""make playlist Y"")

Examples:
User: ""open settings""
Output: {""type"":""open_page"",""args"":{""page"":""settings""},""needsConfirmation"":false,""confirmationText"":null}

User: ""play nature video""
Output: {""type"":""play_video"",""args"":{""query"":""nature"",""scope"":""library""},""needsConfirmation"":false,""confirmationText"":null}

User: ""create playlist favorites""
Output: {""type"":""create_playlist"",""args"":{""name"":""favorites"",""fromScope"":""library""},""needsConfirmation"":true,""confirmationText"":""Create playlist 'favorites' with all videos in library?""}

User: ""import videos""
Output: {""type"":""import"",""args"":{""source"":""picker""},""needsConfirmation"":false,""confirmationText"":null}";

    public ParsedCommand Parse(string jsonResponse)
    {
        try
        {
            var cleanJson = CleanJsonResponse(jsonResponse);
            var doc = JsonDocument.Parse(cleanJson);
            var root = doc.RootElement;

            var command = new ParsedCommand();

            if (root.TryGetProperty("type", out var typeElement))
                command.Type = typeElement.GetString() ?? "unknown";

            if (root.TryGetProperty("args", out var argsElement))
                command.Args = argsElement.Clone();

            if (root.TryGetProperty("needsConfirmation", out var needsConfElement))
                command.NeedsConfirmation = needsConfElement.GetBoolean();

            if (root.TryGetProperty("confirmationText", out var confirmTextElement))
                command.ConfirmationText = confirmTextElement.GetString();

            return command;
        }
        catch
        {
            return new ParsedCommand
            {
                Type = "unknown",
                ConfirmationText = "Failed to parse AI response. Please try rephrasing your command."
            };
        }
    }

    private string CleanJsonResponse(string response)
    {
        var trimmed = response.Trim();
        
        if (trimmed.StartsWith("```json"))
        {
            trimmed = trimmed.Substring(7);
            var endIndex = trimmed.IndexOf("```");
            if (endIndex > 0)
                trimmed = trimmed.Substring(0, endIndex);
        }
        else if (trimmed.StartsWith("```"))
        {
            trimmed = trimmed.Substring(3);
            var endIndex = trimmed.IndexOf("```");
            if (endIndex > 0)
                trimmed = trimmed.Substring(0, endIndex);
        }

        return trimmed.Trim();
    }

    public string GetSystemPrompt() => SystemPrompt;
}
