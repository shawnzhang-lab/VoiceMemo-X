using System.Configuration;
using System.Data;
using System.Windows;
using VoiceMemoryDemo.App.Services;

namespace VoiceMemoryDemo.App;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : System.Windows.Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        UiLanguageService.Initialize();
        base.OnStartup(e);
    }
}

