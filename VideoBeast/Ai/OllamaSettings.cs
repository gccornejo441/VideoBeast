using System;

namespace VideoBeast.Ai;

public sealed class OllamaSettings
{
    public bool Enabled { get; set; }
    public string BaseUrl { get; set; } = "http://localhost:11434";
    public string? DefaultModel { get; set; }

    public OllamaSettings Clone()
    {
        return new OllamaSettings
        {
            Enabled = Enabled,
            BaseUrl = BaseUrl ?? "http://localhost:11434",
            DefaultModel = DefaultModel
        };
    }
}
