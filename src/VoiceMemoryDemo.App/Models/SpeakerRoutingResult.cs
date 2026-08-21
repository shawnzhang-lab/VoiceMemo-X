namespace VoiceMemoryDemo.App.Models;

public enum LocalSpeakerLabel
{
    Single,
    Multi,
    Uncertain,
    NoSpeech
}

public enum MeetingAsrRoute
{
    StandardAsr,
    Speaker20,
    BlockOrReview
}

public sealed record SpeakerRoutingSegment(
    double StartSeconds,
    double EndSeconds,
    int Speaker);

public sealed record SpeakerRoutingResult(
    LocalSpeakerLabel Label,
    MeetingAsrRoute RecommendedRoute,
    MeetingAsrRoute ExecutionRoute,
    double Confidence,
    int DetectedSpeakerCount,
    TimeSpan AudioDuration,
    TimeSpan SpeechDuration,
    TimeSpan AnalysisElapsed,
    string Reason,
    bool ShadowMode,
    IReadOnlyList<SpeakerRoutingSegment> Segments)
{
    public bool UsesSpeaker20 => ExecutionRoute == MeetingAsrRoute.Speaker20;
}
