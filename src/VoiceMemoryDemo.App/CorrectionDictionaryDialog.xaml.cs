using System.Collections.ObjectModel;
using System.Windows;
using Microsoft.Data.Sqlite;
using VoiceMemoryDemo.App.Models;
using VoiceMemoryDemo.App.Services;

namespace VoiceMemoryDemo.App;

public partial class CorrectionDictionaryDialog : Window
{
    private readonly MemoryRepository _repository;
    private readonly ObservableCollection<CorrectionItem> _items = [];
    private long? _editingId;
    private CorrectionItem? _pendingDelete;

    public CorrectionDictionaryDialog(MemoryRepository repository)
    {
        InitializeComponent();
        _repository = repository;
        CorrectionList.ItemsSource = _items;
        UiLanguageService.Apply(this);
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await RefreshAsync();
    }

    private void LocalizedElement_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is DependencyObject element) UiLanguageService.Apply(element);
    }

    private async void SaveCorrectionButton_Click(object sender, RoutedEventArgs e)
    {
        var spoken = SpokenTextBox.Text.Trim();
        var preferred = PreferredTextBox.Text.Trim();
        if (spoken.Length == 0 || preferred.Length == 0)
        {
            ShowStatus("请填写识别写法和固定写法。", error: true);
            return;
        }

        try
        {
            if (_editingId is long id)
            {
                await _repository.UpdateCorrectionAsync(id, spoken, preferred);
                ShowStatus("纠错词已经更新。", error: false);
            }
            else
            {
                await _repository.AddCorrectionAsync(spoken, preferred);
                ShowStatus("新的纠错词已经保存。", error: false);
            }
            ClearEditor();
            await RefreshAsync();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode == 19)
        {
            ShowStatus("这个识别写法已经存在，请直接编辑原有记录。", error: true);
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Write("CorrectionDictionarySave", ex);
            ShowStatus("保存失败，请稍后重试。", error: true);
        }
    }

    private void EditCorrectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CorrectionItem item }) return;
        _editingId = item.Id;
        SpokenTextBox.Text = item.Spoken;
        PreferredTextBox.Text = item.Preferred;
        SaveCorrectionButton.Content = UiLanguageService.Text("保存修改", "Save changes");
        CancelEditButton.Visibility = Visibility.Visible;
        DeleteConfirmationPanel.Visibility = Visibility.Collapsed;
        SpokenTextBox.Focus();
        SpokenTextBox.SelectAll();
        ShowStatus(UiLanguageService.IsEnglish
            ? $"Editing: {item.Spoken} → {item.Preferred}"
            : $"正在编辑：{item.Spoken} → {item.Preferred}", error: false);
    }

    private void CancelEditButton_Click(object sender, RoutedEventArgs e)
    {
        ClearEditor();
        ShowStatus("已取消编辑。", error: false);
    }

    private void DeleteCorrectionButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: CorrectionItem item }) return;
        _pendingDelete = item;
        DeleteConfirmationText.Text = UiLanguageService.IsEnglish
            ? $"Delete “{item.Spoken} → {item.Preferred}”?"
            : $"删除“{item.Spoken} → {item.Preferred}”？";
        DeleteConfirmationPanel.Visibility = Visibility.Visible;
    }

    private void CancelDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingDelete = null;
        DeleteConfirmationPanel.Visibility = Visibility.Collapsed;
    }

    private async void ConfirmDeleteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_pendingDelete is not { } item) return;
        try
        {
            await _repository.DeleteCorrectionAsync(item.Id);
            _pendingDelete = null;
            DeleteConfirmationPanel.Visibility = Visibility.Collapsed;
            if (_editingId == item.Id) ClearEditor();
            await RefreshAsync();
            ShowStatus("纠错词已经删除。", error: false);
        }
        catch (Exception ex)
        {
            DiagnosticLogService.Write("CorrectionDictionaryDelete", ex);
            ShowStatus("删除失败，请稍后重试。", error: true);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private async Task RefreshAsync()
    {
        var corrections = await _repository.GetCorrectionsAsync();
        _items.Clear();
        foreach (var item in corrections) _items.Add(item);
        CorrectionCountText.Text = UiLanguageService.IsEnglish
            ? $"{corrections.Count} saved"
            : $"已记住 {corrections.Count} 条";
        EmptyPanel.Visibility = corrections.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CorrectionList.Visibility = corrections.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private void ClearEditor()
    {
        _editingId = null;
        SpokenTextBox.Clear();
        PreferredTextBox.Clear();
        SaveCorrectionButton.Content = UiLanguageService.Text("新增纠错", "Add correction");
        CancelEditButton.Visibility = Visibility.Collapsed;
    }

    private void ShowStatus(string message, bool error)
    {
        StatusText.Text = UiLanguageService.Translate(message);
        StatusText.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter()
            .ConvertFromString(error ? "#FF8D95" : "#75D8AA")!;
    }
}
