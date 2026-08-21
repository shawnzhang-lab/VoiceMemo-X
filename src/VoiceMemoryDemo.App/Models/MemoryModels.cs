namespace VoiceMemoryDemo.App.Models;

public sealed record MemoryItem(long Id, string Kind, string Content, DateTime CreatedUtc);

public sealed record CorrectionItem(
    long Id,
    string Spoken,
    string Preferred,
    int UseCount,
    DateTime CreatedUtc);

public sealed record TextHistoryItem(
    long Id,
    string RawText,
    string FinalText,
    string AppName,
    DateTime CreatedUtc);

public sealed record UnembeddedHistoryItem(
    long Id,
    string FinalText,
    string AppName,
    DateTime CreatedUtc);

public sealed class MemoryContext
{
    public IReadOnlyList<string> Preferences { get; init; } = [];
    public IReadOnlyList<string> RecentExamples { get; init; } = [];
    public IReadOnlyDictionary<string, string> Corrections { get; init; } =
        new Dictionary<string, string>();
}
