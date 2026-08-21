using VoiceMemoryDemo.App.Services;
using Microsoft.Data.Sqlite;

var tempDirectory = Path.Combine(Path.GetTempPath(), "VoiceMemoryDemo-MemorySmoke", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tempDirectory);
try
{
    var repository = new MemoryRepository(Path.Combine(tempDirectory, "memory.db"));
    await repository.InitializeAsync();

    await repository.AddCorrectionAsync("扣的可斯", "Codex");
    var corrections = await repository.GetCorrectionsAsync();
    if (corrections.Count != 1 || corrections[0].Spoken != "扣的可斯" || corrections[0].Preferred != "Codex")
    {
        throw new InvalidOperationException("Correction creation or read failed.");
    }

    await repository.UpdateCorrectionAsync(corrections[0].Id, "扣得可斯", "Codex");
    corrections = await repository.GetCorrectionsAsync();
    if (corrections.Count != 1 || corrections[0].Spoken != "扣得可斯")
    {
        throw new InvalidOperationException("Correction update failed.");
    }

    await repository.DeleteCorrectionAsync(corrections[0].Id);
    corrections = await repository.GetCorrectionsAsync();
    if (corrections.Count != 0)
    {
        throw new InvalidOperationException("Correction deletion failed.");
    }

    Console.WriteLine("PASS: correction dictionary create/read/update/delete.");
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("FAIL: " + ex);
    return 1;
}
finally
{
    SqliteConnection.ClearAllPools();
    if (Directory.Exists(tempDirectory)) Directory.Delete(tempDirectory, recursive: true);
}
