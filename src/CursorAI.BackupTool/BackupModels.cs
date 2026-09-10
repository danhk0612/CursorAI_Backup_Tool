using System.Text.Json.Serialization;

namespace CursorAI.BackupTool;

internal sealed class BackupInfo
{
    [JsonPropertyName("backupVersion")]
    public int BackupVersion { get; set; } = 2;

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("cursorVersion")]
    public string? CursorVersion { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "normal";

    [JsonPropertyName("workspaceStorageIncluded")]
    public bool WorkspaceStorageIncluded { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "SUCCESS";
}

internal sealed class BackupRecord
{
    public required string Name { get; init; }
    public required string FullPath { get; init; }
    public required string Type { get; init; }
    public required string Status { get; init; }
    public required string SizeText { get; init; }
    public DateTime CreatedAt { get; init; }
    public bool IsIncomplete { get; init; }

    public string CreatedText => CreatedAt == DateTime.MinValue ? "-" : CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
}

internal sealed record OperationResult(bool Success, string Message);
