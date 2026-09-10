using System.Text.Json.Serialization;

namespace CursorAI.BackupTool;

internal sealed class BackupInfo
{
    [JsonPropertyName("backupVersion")]
    public int BackupVersion { get; set; } = 3;

    [JsonPropertyName("storageFormat")]
    public string StorageFormat { get; set; } = "archive-v3";

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

    [JsonPropertyName("totalBytes")]
    public long TotalBytes { get; set; }
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
    public int BackupVersion { get; init; }
    public string? StorageFormat { get; init; }
    public bool WorkspaceStorageIncluded { get; init; }

    public bool IsArchiveV3 => BackupVersion >= 3 && string.Equals(StorageFormat, "archive-v3", StringComparison.OrdinalIgnoreCase);
    public string CreatedText => CreatedAt == DateTime.MinValue ? "-" : CreatedAt.ToString("yyyy-MM-dd HH:mm:ss");
}

internal sealed record OperationProgress(int Percent, string Message);
internal sealed record OperationResult(bool Success, string Message);

internal sealed record CursorInstall(string? ExecutablePath, string? CliPath);