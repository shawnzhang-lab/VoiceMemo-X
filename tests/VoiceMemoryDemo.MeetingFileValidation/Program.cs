using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VoiceMemoryDemo.App.Services;

if (args.Length == 0)
{
    Console.Error.WriteLine("用法：VoiceMemoryDemo.MeetingFileValidation <媒体文件> [报告目录]");
    return 2;
}

var mediaPath = Path.GetFullPath(args[0]);
var reportDirectory = args.Length > 1
    ? Path.GetFullPath(args[1])
    : Path.Combine(AppContext.BaseDirectory, "validation-results");
var settings = new SecureSettingsStore().Load();
var progress = new Progress<string>(message => Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] {message}"));

try
{
    var service = new MeetingFileValidationService();
    var result = await service.RunAsync(mediaPath, settings, reportDirectory, progress);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine(ex.ToString());
    return 1;
}
