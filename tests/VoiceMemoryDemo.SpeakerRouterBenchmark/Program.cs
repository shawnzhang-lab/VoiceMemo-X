using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VoiceMemoryDemo.App.Models;
using VoiceMemoryDemo.App.Services;

if (args.Length > 0 && string.Equals(args[0], "--full-probe", StringComparison.OrdinalIgnoreCase))
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("Usage: --full-probe <audio-file> [audio-file ...]");
        return 2;
    }
    foreach (var file in args.Skip(1))
    {
        await FullDiarizationProbe.RunAsync(Path.GetFullPath(file));
    }
    return 0;
}

if (args.Length == 0)
{
    Console.Error.WriteLine("Usage: SpeakerRouterBenchmark <manifest.json> [output-directory]");
    return 2;
}

var manifestPath = Path.GetFullPath(args[0]);
if (!File.Exists(manifestPath))
{
    Console.Error.WriteLine($"Manifest not found: {manifestPath}");
    return 2;
}

var outputDirectory = args.Length >= 2
    ? Path.GetFullPath(args[1])
    : Path.Combine(Environment.CurrentDirectory, "test-results", "speaker-router");
Directory.CreateDirectory(outputDirectory);

var jsonOptions = new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    WriteIndented = true,
    Converters = { new JsonStringEnumConverter() }
};
var manifest = JsonSerializer.Deserialize<BenchmarkManifest>(
                   await File.ReadAllTextAsync(manifestPath),
                   jsonOptions)
               ?? throw new InvalidOperationException("Manifest is empty or invalid.");
if (manifest.Samples.Count == 0)
{
    Console.Error.WriteLine("Manifest has no samples.");
    return 2;
}

var manifestDirectory = Path.GetDirectoryName(manifestPath)!;
var router = new LocalSpeakerRouterService();
if (!router.ModelFilesAvailable)
{
    Console.Error.WriteLine("Speaker router model files are missing.");
    return 2;
}

var rows = new List<BenchmarkRow>();
foreach (var sample in manifest.Samples)
{
    var path = Path.IsPathRooted(sample.Path)
        ? sample.Path
        : Path.GetFullPath(Path.Combine(manifestDirectory, sample.Path));
    if (!File.Exists(path))
    {
        rows.Add(BenchmarkRow.Missing(sample, path));
        Console.WriteLine($"MISS {sample.Id}: {path}");
        continue;
    }

    Console.WriteLine($"RUN  {sample.Id}: {Path.GetFileName(path)}");
    try
    {
        var result = await router.AnalyzeAsync(path);
        var row = BenchmarkRow.From(sample, path, result);
        rows.Add(row);
        Console.WriteLine(
            $"  => {row.PredictedLabel}, route={row.ExecutionRoute}, " +
            $"speakers={row.DetectedSpeakerCount}, confidence={row.Confidence:F3}, " +
            $"elapsed={row.ElapsedMilliseconds:F0}ms");
    }
    catch (Exception ex)
    {
        rows.Add(BenchmarkRow.Error(sample, path, ex));
        Console.WriteLine($"  => ERROR {ex.GetType().Name}: {ex.Message}");
    }
}

var stamp = DateTimeOffset.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
var jsonPath = Path.Combine(outputDirectory, $"speaker-router-{stamp}.json");
var csvPath = Path.Combine(outputDirectory, $"speaker-router-{stamp}.csv");
var summaryPath = Path.Combine(outputDirectory, $"speaker-router-{stamp}-summary.md");
await File.WriteAllTextAsync(jsonPath, JsonSerializer.Serialize(rows, jsonOptions), new UTF8Encoding(false));
await File.WriteAllTextAsync(csvPath, BuildCsv(rows), new UTF8Encoding(false));
await File.WriteAllTextAsync(summaryPath, BuildSummary(rows), new UTF8Encoding(false));

Console.WriteLine($"JSON: {jsonPath}");
Console.WriteLine($"CSV:  {csvPath}");
Console.WriteLine($"MD:   {summaryPath}");
return rows.Any(row => row.Status != "ok") ? 1 : 0;

static string BuildCsv(IEnumerable<BenchmarkRow> rows)
{
    var builder = new StringBuilder();
    builder.AppendLine("id,path,status,groundTruth,predictedLabel,recommendedRoute,executionRoute,confidence,detectedSpeakerCount,audioSeconds,speechSeconds,elapsedMilliseconds,reason,category,language,notes");
    foreach (var row in rows)
    {
        builder.AppendLine(string.Join(',', new[]
        {
            row.Id,
            row.Path,
            row.Status,
            row.GroundTruth,
            row.PredictedLabel,
            row.RecommendedRoute,
            row.ExecutionRoute,
            row.Confidence.ToString("F6", CultureInfo.InvariantCulture),
            row.DetectedSpeakerCount.ToString(CultureInfo.InvariantCulture),
            row.AudioSeconds.ToString("F3", CultureInfo.InvariantCulture),
            row.SpeechSeconds.ToString("F3", CultureInfo.InvariantCulture),
            row.ElapsedMilliseconds.ToString("F1", CultureInfo.InvariantCulture),
            row.Reason,
            row.Category,
            row.Language,
            row.Notes
        }.Select(Csv)));
    }
    return builder.ToString();
}

static string BuildSummary(IReadOnlyList<BenchmarkRow> rows)
{
    var ok = rows.Where(row => row.Status == "ok").ToArray();
    var multi = ok.Where(row => row.GroundTruth == "Multi").ToArray();
    var single = ok.Where(row => row.GroundTruth == "Single").ToArray();
    var noSpeech = ok.Where(row => row.GroundTruth == "NoSpeech").ToArray();
    var multiSafe = multi.Count(row => row.RecommendedRoute is nameof(MeetingAsrRoute.Speaker20) or nameof(MeetingAsrRoute.BlockOrReview));
    var singleStandard = single.Count(row => row.RecommendedRoute == nameof(MeetingAsrRoute.StandardAsr));
    var elapsed = ok.Select(row => row.ElapsedMilliseconds).Order().ToArray();
    var p95 = elapsed.Length == 0 ? 0 : elapsed[(int)Math.Ceiling(elapsed.Length * 0.95) - 1];
    return $$"""
        # Speaker router benchmark

        - Samples: {{rows.Count}} (ok {{ok.Length}}, failed/missing {{rows.Count - ok.Length}})
        - Multi safe recall: {{Ratio(multiSafe, multi.Length)}} ({{multiSafe}}/{{multi.Length}})
        - Single specificity recommendation: {{Ratio(singleStandard, single.Length)}} ({{singleStandard}}/{{single.Length}})
        - Analysis elapsed P95: {{p95:F0}} ms
        - Shadow mode: true; the app still executes Speaker 2.0 during this phase.

        ## Label confusion matrix

        | Ground truth | Single | Multi | Uncertain | NoSpeech |
        |---|---:|---:|---:|---:|
        {{MatrixRow("Single", single, "PredictedLabel")}}
        {{MatrixRow("Multi", multi, "PredictedLabel")}}
        {{MatrixRow("NoSpeech", noSpeech, "PredictedLabel")}}

        ## Candidate route matrix (used for the go/no-go decision)

        | Ground truth | StandardAsr | Speaker20 | BlockOrReview |
        |---|---:|---:|---:|
        {{CandidateRouteRow("Single", single)}}
        {{CandidateRouteRow("Multi", multi)}}
        {{CandidateRouteRow("NoSpeech", noSpeech)}}

        The application remains in shadow mode, so the actual execution route is still Speaker 2.0 for every import. The matrix above evaluates the route that would be used after shadow mode is disabled.
        """;
}

static string MatrixRow(string label, IEnumerable<BenchmarkRow> rows, string _) =>
    $"| {label} | {rows.Count(row => row.PredictedLabel == "Single")} | " +
    $"{rows.Count(row => row.PredictedLabel == "Multi")} | " +
    $"{rows.Count(row => row.PredictedLabel == "Uncertain")} | " +
    $"{rows.Count(row => row.PredictedLabel == "NoSpeech")} |";

static string CandidateRouteRow(string label, IEnumerable<BenchmarkRow> rows) =>
    $"| {label} | {rows.Count(row => row.RecommendedRoute == "StandardAsr")} | " +
    $"{rows.Count(row => row.RecommendedRoute == "Speaker20")} | " +
    $"{rows.Count(row => row.RecommendedRoute == "BlockOrReview")} |";

static string Ratio(int numerator, int denominator) =>
    denominator == 0 ? "n/a" : (numerator / (double)denominator).ToString("P1", CultureInfo.InvariantCulture);

static string Csv(string value)
{
    var escaped = value.Replace("\"", "\"\"");
    return escaped.IndexOfAny([',', '\"', '\r', '\n']) >= 0 ? $"\"{escaped}\"" : escaped;
}

public sealed record BenchmarkManifest(List<BenchmarkSample> Samples)
{
    public BenchmarkManifest() : this([]) { }
}

public sealed record BenchmarkSample(
    string Id,
    string Path,
    int SpeakerCount,
    string Category,
    string Language,
    string SourceUrl,
    string License,
    string Notes)
{
    public BenchmarkSample() : this("", "", 0, "", "", "", "", "") { }
}

public sealed record BenchmarkRow(
    string Id,
    string Path,
    string Status,
    string GroundTruth,
    string PredictedLabel,
    string RecommendedRoute,
    string ExecutionRoute,
    double Confidence,
    int DetectedSpeakerCount,
    double AudioSeconds,
    double SpeechSeconds,
    double ElapsedMilliseconds,
    string Reason,
    string Category,
    string Language,
    string Notes)
{
    public static BenchmarkRow From(BenchmarkSample sample, string path, SpeakerRoutingResult result) => new(
        sample.Id,
        path,
        "ok",
        GroundTruthOf(sample),
        result.Label.ToString(),
        result.RecommendedRoute.ToString(),
        result.ExecutionRoute.ToString(),
        result.Confidence,
        result.DetectedSpeakerCount,
        result.AudioDuration.TotalSeconds,
        result.SpeechDuration.TotalSeconds,
        result.AnalysisElapsed.TotalMilliseconds,
        result.Reason,
        sample.Category,
        sample.Language,
        sample.Notes);

    public static BenchmarkRow Missing(BenchmarkSample sample, string path) => Failure(sample, path, "missing", "file_missing");
    public static BenchmarkRow Error(BenchmarkSample sample, string path, Exception ex) => Failure(sample, path, "error", $"{ex.GetType().Name}:{ex.Message}");

    private static BenchmarkRow Failure(BenchmarkSample sample, string path, string status, string reason) => new(
        sample.Id,
        path,
        status,
        GroundTruthOf(sample),
        "",
        "",
        "",
        0,
        0,
        0,
        0,
        0,
        reason,
        sample.Category,
        sample.Language,
        sample.Notes);

    private static string GroundTruthOf(BenchmarkSample sample) =>
        sample.SpeakerCount <= 0 || string.Equals(sample.Category, "no_speech", StringComparison.OrdinalIgnoreCase)
            ? "NoSpeech"
            : sample.SpeakerCount == 1
                ? "Single"
                : "Multi";
}
