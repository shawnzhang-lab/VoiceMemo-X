using System.Text.RegularExpressions;

namespace VoiceMemoryDemo.App.Services;

public static class MeetingTranscriptQualityService
{
    public static bool HasMeaningfulContent(string transcript)
    {
        if (string.IsNullOrWhiteSpace(transcript)) return false;
        var withoutLabels = Regex.Replace(
            transcript,
            @"(?im)^\s*(?:Speaker\s+(?:\d+|pending)|说话人(?:\d+|待定))\s*[:：]\s*",
            string.Empty);
        return withoutLabels.Count(char.IsLetterOrDigit) >= 4;
    }
}
