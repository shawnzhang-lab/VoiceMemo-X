using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using VoiceMemoryDemo.App.Models;

namespace VoiceMemoryDemo.App.Services;

public sealed class SecureSettingsStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("VoiceMemoryDemo.Settings.v1");
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    public AppSettings Load()
    {
        AppPaths.EnsureCreated();
        if (!File.Exists(AppPaths.SettingsPath))
        {
            return new AppSettings();
        }

        try
        {
            var encrypted = File.ReadAllBytes(AppPaths.SettingsPath);
            var clear = ProtectedData.Unprotect(encrypted, Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<AppSettings>(clear, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        AppPaths.EnsureCreated();
        var clear = JsonSerializer.SerializeToUtf8Bytes(settings, JsonOptions);
        var encrypted = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(AppPaths.SettingsPath, encrypted);
        CryptographicOperations.ZeroMemory(clear);
    }
}
