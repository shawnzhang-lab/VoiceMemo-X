using System.Windows;
using VoiceMemoryDemo.App.Services;

namespace VoiceMemoryDemo.App;

public partial class DeleteMeetingReportDialog : Window
{
    public DeleteMeetingReportDialog(MeetingReportItem report)
    {
        InitializeComponent();
        UiLanguageService.Apply(this);
        ReportTitleText.Text = report.Title;
        ReportMetaText.Text = $"{report.CreatedAtText} · {report.DurationText} · {report.FileName}";
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void DeleteButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
