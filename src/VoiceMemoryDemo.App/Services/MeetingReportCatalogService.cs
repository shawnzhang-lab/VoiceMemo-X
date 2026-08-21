using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.VisualBasic.FileIO;

namespace VoiceMemoryDemo.App.Services;

public sealed record MeetingReportItem(
    string FullPath,
    string FileName,
    string Title,
    string CreatedAtText,
    string DurationText,
    string Summary,
    long ByteLength,
    DateTime LastWriteTime);

public sealed record MeetingProcessingTaskItem(
    string Id,
    string Title,
    string Stage,
    string Detail,
    int Percent);

public static partial class MeetingReportCatalogService
{
    public static async Task<IReadOnlyList<MeetingReportItem>> LoadAsync(
        string? directory = null,
        CancellationToken cancellationToken = default)
    {
        directory ??= AppPaths.MeetingsDirectory;
        if (!Directory.Exists(directory)) return [];

        var files = new DirectoryInfo(directory)
            .EnumerateFiles("*.md", System.IO.SearchOption.TopDirectoryOnly)
            .OrderByDescending(file => file.LastWriteTime)
            .ToArray();
        var reports = new List<MeetingReportItem>(files.Length);
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var head = await ReadHeadAsync(file.FullName, cancellationToken);
                reports.Add(CreateItem(file, head));
            }
            catch (IOException ex)
            {
                DiagnosticLogService.Write("MeetingReportCatalog", ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                DiagnosticLogService.Write("MeetingReportCatalog", ex);
            }
        }
        return reports;
    }

    public static Task<string> ReadAsync(
        MeetingReportItem report,
        CancellationToken cancellationToken = default) =>
        File.ReadAllTextAsync(report.FullPath, cancellationToken);

    public static void MoveToRecycleBin(
        MeetingReportItem report,
        string? reportsDirectory = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        reportsDirectory ??= AppPaths.MeetingsDirectory;

        var reportsRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(reportsDirectory));
        var reportPath = Path.GetFullPath(report.FullPath);
        var reportDirectory = Path.TrimEndingDirectorySeparator(
            Path.GetDirectoryName(reportPath) ?? string.Empty);

        if (!string.Equals(reportDirectory, reportsRoot, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(Path.GetExtension(reportPath), ".md", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("只能删除会议报告目录中的 Markdown 报告。");
        }

        if (!File.Exists(reportPath))
        {
            throw new FileNotFoundException("会议报告已经不存在，可能已被其他程序移动或删除。", reportPath);
        }

        FileSystem.DeleteFile(
            reportPath,
            UIOption.OnlyErrorDialogs,
            RecycleOption.SendToRecycleBin);
    }

    public static string ToPlainText(string markdown)
    {
        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var output = new StringBuilder(markdown.Length);
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd();
            if (line == "---")
            {
                output.AppendLine(new string('—', 24));
                continue;
            }

            line = HeadingPrefixRegex().Replace(line, string.Empty);
            line = BoldMarkerRegex().Replace(line, "$1");
            if (line.StartsWith('|') && line.EndsWith('|'))
            {
                if (TableDividerRegex().IsMatch(line)) continue;
                line = string.Join("  |  ", line.Trim('|').Split('|').Select(cell => cell.Trim()));
            }
            output.AppendLine(line);
        }
        return output.ToString().TrimEnd();
    }

    private static async Task<string> ReadHeadAsync(string path, CancellationToken cancellationToken)
    {
        const int maxCharacters = 48 * 1024;
        var buffer = new char[4096];
        var builder = new StringBuilder();
        using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        while (builder.Length < maxCharacters)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(0, Math.Min(buffer.Length, maxCharacters - builder.Length)), cancellationToken);
            if (count <= 0) break;
            builder.Append(buffer, 0, count);
            var current = builder.ToString();
            if (current.Contains("## 原始转写附录", StringComparison.Ordinal) ||
                current.Contains("## Full transcript appendix", StringComparison.Ordinal)) break;
        }
        return builder.ToString();
    }

    private static MeetingReportItem CreateItem(FileInfo file, string documentHead)
    {
        var createdAt = ParseStartTime(file.Name, documentHead) ?? file.LastWriteTime;
        var imported = documentHead.Contains("- 原始文件：", StringComparison.Ordinal) ||
                       documentHead.Contains("- Source file:", StringComparison.Ordinal);
        var duration = imported
            ? ExtractFirstLineValue(documentHead, "- 音频时长：", "- Audio duration:")
            : ExtractFirstLineValue(documentHead, "- 记录时长：", "- Duration:");
        var subject = ExtractFirstLineValue(documentHead, "- 会议主题：", "- Meeting topic:");
        if (string.IsNullOrWhiteSpace(subject))
            subject = ExtractFirstLineValue(documentHead, "- 会议性质：", "- Meeting type:");
        if (string.IsNullOrWhiteSpace(subject) && imported)
        {
            subject = ExtractFirstLineValue(documentHead, "- 原始文件：", "- Source file:");
        }
        if (string.IsNullOrWhiteSpace(subject))
            subject = imported
                ? UiLanguageService.Text("导入会议", "Imported meeting")
                : UiLanguageService.Text("实时会议", "Live meeting");
        var summary = ExtractSection(documentHead, "## 核心摘要");
        if (string.IsNullOrWhiteSpace(summary)) summary = ExtractSection(documentHead, "## Core summary");
        if (string.IsNullOrWhiteSpace(summary))
            summary = UiLanguageService.Text("报告已经生成，点击后可查看完整内容。", "The report is ready. Open it to view the full content.");
        if (summary.Length > 150) summary = summary[..150].TrimEnd() + "…";

        return new MeetingReportItem(
            file.FullName,
            file.Name,
            subject,
            createdAt.ToString("yyyy-MM-dd HH:mm"),
            string.IsNullOrWhiteSpace(duration)
                ? UiLanguageService.Text("时长待确认", "Duration to confirm")
                : duration,
            summary,
            file.Length,
            file.LastWriteTime);
    }

    private static DateTime? ParseStartTime(string fileName, string documentHead)
    {
        var prefix = fileName.Length >= 15 ? fileName[..15] : string.Empty;
        if (DateTime.TryParseExact(prefix, "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal, out var fileTime))
        {
            return fileTime;
        }

        foreach (var marker in new[] { "- 开始时间：", "- 导入时间：", "- Start time:", "- Imported at:" })
        {
            var value = ExtractLineValue(documentHead, marker);
            if (DateTime.TryParse(value, CultureInfo.CurrentCulture, DateTimeStyles.AssumeLocal, out var parsed))
            {
                return parsed;
            }
        }
        return null;
    }

    private static string ExtractLineValue(string text, string marker)
    {
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start += marker.Length;
        var end = text.IndexOfAny(['\r', '\n'], start);
        return (end < 0 ? text[start..] : text[start..end]).Trim();
    }

    private static string ExtractFirstLineValue(string text, params string[] markers)
    {
        foreach (var marker in markers)
        {
            var value = ExtractLineValue(text, marker);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return string.Empty;
    }

    private static string ExtractSection(string text, string heading)
    {
        var start = text.IndexOf(heading, StringComparison.Ordinal);
        if (start < 0) return string.Empty;
        start += heading.Length;
        while (start < text.Length && (text[start] == '\r' || text[start] == '\n')) start++;
        var end = text.IndexOf("\n## ", start, StringComparison.Ordinal);
        var section = (end < 0 ? text[start..] : text[start..end]).Trim();
        return string.Join(" ", section.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim()));
    }

    [GeneratedRegex(@"^#{1,6}\s+")]
    private static partial Regex HeadingPrefixRegex();

    [GeneratedRegex(@"\*\*(.+?)\*\*")]
    private static partial Regex BoldMarkerRegex();

    [GeneratedRegex(@"^\|?\s*:?-{3,}", RegexOptions.Compiled)]
    private static partial Regex TableDividerRegex();
}
