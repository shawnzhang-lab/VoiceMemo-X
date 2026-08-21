using System.Text;
using System.Windows.Documents;
using VoiceMemoryDemo.App.Models;
using VoiceMemoryDemo.App.Services;

var tempDirectory = Path.Combine(Path.GetTempPath(), "VoiceMemoryDemo-MeetingReportSmoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDirectory);
try
{
    if (MeetingSummaryTemplateDefaults.Validate(MeetingSummaryTemplateDefaults.DefaultText) is not null)
    {
        throw new InvalidOperationException("Built-in meeting summary template is invalid.");
    }
    if (MeetingSummaryTemplateDefaults.NormalizeForStorage(MeetingSummaryTemplateDefaults.DefaultText).Length != 0)
    {
        throw new InvalidOperationException("Restoring the default template should use the built-in template marker.");
    }
    if (MeetingSummaryTemplateDefaults.Validate(
            MeetingSummaryTemplateDefaults.DefaultEnglishText,
            "en") is not null)
    {
        throw new InvalidOperationException("Built-in English meeting summary template is invalid.");
    }
    if (MeetingSummaryTemplateDefaults.NormalizeForStorage(
            MeetingSummaryTemplateDefaults.DefaultEnglishText,
            "en").Length != 0)
    {
        throw new InvalidOperationException("Restoring the English default template should use the built-in template marker.");
    }

    var path = Path.Combine(tempDirectory, "20260817-224201-会议纪要.md");
    var markdown = """
        # 会议纪要

        ## 会议概览
        - 会议性质：声音测试

        ## 核心摘要
        两位参与者完成了声音测试，并确认后续查看报告。

        ## 行动项
        | 事项 | 负责人 |
        |------|--------|
        | 查看报告 | 说话人1 |

        ---

        ## 记录信息
        - 开始时间：2026-08-17 22:42:01
        - 记录时长：1 分钟

        ## 原始转写附录
        说话人1：测试。
        """;
    await File.WriteAllTextAsync(path, markdown, new UTF8Encoding(false));

    var reports = await MeetingReportCatalogService.LoadAsync(tempDirectory);
    if (reports.Count != 1) throw new InvalidOperationException($"Expected 1 report, got {reports.Count}.");
    var report = reports[0];
    if (report.Title != "声音测试") throw new InvalidOperationException("Report title parsing failed.");
    if (report.DurationText != "1 分钟") throw new InvalidOperationException("Duration parsing failed.");
    if (!report.Summary.Contains("完成了声音测试", StringComparison.Ordinal)) throw new InvalidOperationException("Summary parsing failed.");

    var englishPath = Path.Combine(tempDirectory, "20260818-101500-English-call-meeting-report.md");
    var englishMarkdown = """
        # Meeting report

        ## Meeting overview
        - Meeting topic: English localization review

        ## Core summary
        The team verified that the English template and overlay copy use English throughout.

        ---

        ## Recording details
        - Start time: 2026-08-18 10:15:00
        - Duration: 4m 30s

        ## Full transcript appendix
        Speaker 1: Test complete.
        """;
    await File.WriteAllTextAsync(englishPath, englishMarkdown, new UTF8Encoding(false));
    var bilingualReports = await MeetingReportCatalogService.LoadAsync(tempDirectory);
    var englishReport = bilingualReports.Single(item => item.FullPath == englishPath);
    if (englishReport.Title != "English localization review") throw new InvalidOperationException("English report title parsing failed.");
    if (englishReport.DurationText != "4m 30s") throw new InvalidOperationException("English duration parsing failed.");
    if (!englishReport.Summary.Contains("English template", StringComparison.Ordinal)) throw new InvalidOperationException("English summary parsing failed.");

    var plainText = MeetingReportCatalogService.ToPlainText(markdown);
    if (plainText.Contains("##", StringComparison.Ordinal) || plainText.Contains("|------", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Plain-text export still contains Markdown structure markers.");
    }
    if (!plainText.Contains("查看报告  |  说话人1", StringComparison.Ordinal))
    {
        throw new InvalidOperationException("Plain-text table conversion failed.");
    }

    var document = MeetingReportDocumentRenderer.Create(markdown);
    if (!document.Blocks.OfType<Table>().Any())
    {
        throw new InvalidOperationException("Rendered report does not contain an action-item table.");
    }
    if (!document.Blocks.OfType<System.Windows.Documents.List>().Any())
    {
        throw new InvalidOperationException("Rendered report does not contain a semantic list.");
    }

    var outsideDirectory = Path.Combine(tempDirectory, "outside");
    Directory.CreateDirectory(outsideDirectory);
    var outsidePath = Path.Combine(outsideDirectory, "do-not-delete.md");
    await File.WriteAllTextAsync(outsidePath, "protected", new UTF8Encoding(false));
    var outsideReport = report with { FullPath = outsidePath };
    try
    {
        MeetingReportCatalogService.MoveToRecycleBin(outsideReport, tempDirectory);
        throw new InvalidOperationException("Deletion path boundary accepted an outside report.");
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("只能删除", StringComparison.Ordinal))
    {
        // Expected: deletion is limited to a top-level Markdown report in the report directory.
    }
    if (!File.Exists(outsidePath)) throw new InvalidOperationException("Outside report was unexpectedly deleted.");

    MeetingReportCatalogService.MoveToRecycleBin(report, tempDirectory);
    if (File.Exists(path)) throw new InvalidOperationException("Report was not moved to the Recycle Bin.");
    MeetingReportCatalogService.MoveToRecycleBin(englishReport, tempDirectory);
    if (File.Exists(englishPath)) throw new InvalidOperationException("English report was not moved to the Recycle Bin.");

    Console.WriteLine($"PASS: title={report.Title}; duration={report.DurationText}; summary={report.Summary}");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 1;
}
finally
{
    if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
}
