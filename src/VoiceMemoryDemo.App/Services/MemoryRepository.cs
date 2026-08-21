using Microsoft.Data.Sqlite;
using VoiceMemoryDemo.App.Models;

namespace VoiceMemoryDemo.App.Services;

public sealed class MemoryRepository
{
    private readonly string _connectionString;

    public MemoryRepository(string? databasePath = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            AppPaths.EnsureCreated();
            databasePath = AppPaths.DatabasePath;
        }
        else
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(databasePath));
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        }
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync()
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            CREATE TABLE IF NOT EXISTS memory_items (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                kind TEXT NOT NULL,
                content TEXT NOT NULL,
                enabled INTEGER NOT NULL DEFAULT 1,
                created_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS corrections (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                spoken TEXT NOT NULL UNIQUE,
                preferred TEXT NOT NULL,
                use_count INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS text_history (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                raw_text TEXT NOT NULL,
                final_text TEXT NOT NULL,
                app_name TEXT NOT NULL,
                created_utc TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS semantic_embeddings (
                history_id INTEGER PRIMARY KEY,
                model_id TEXT NOT NULL,
                dimensions INTEGER NOT NULL,
                embedding BLOB NOT NULL,
                created_utc TEXT NOT NULL,
                FOREIGN KEY(history_id) REFERENCES text_history(id) ON DELETE CASCADE
            );
            CREATE INDEX IF NOT EXISTS idx_history_app_created
                ON text_history(app_name, created_utc DESC);
            """;
        await command.ExecuteNonQueryAsync();
        await PruneHistoryAsync(connection);
    }

    public async Task AddPreferenceAsync(string content)
    {
        content = content.Trim();
        if (content.Length == 0) return;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO memory_items(kind, content, enabled, created_utc)
            VALUES ('preference', $content, 1, $created);
            """;
        command.Parameters.AddWithValue("$content", content);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task AddCorrectionAsync(string spoken, string preferred)
    {
        spoken = spoken.Trim();
        preferred = preferred.Trim();
        if (spoken.Length == 0 || preferred.Length == 0) return;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO corrections(spoken, preferred, created_utc)
            VALUES ($spoken, $preferred, $created)
            ON CONFLICT(spoken) DO UPDATE SET preferred = excluded.preferred;
            """;
        command.Parameters.AddWithValue("$spoken", spoken);
        command.Parameters.AddWithValue("$preferred", preferred);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<CorrectionItem>> GetCorrectionsAsync(int limit = 500)
    {
        var items = new List<CorrectionItem>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, spoken, preferred, use_count, created_utc
            FROM corrections
            ORDER BY use_count DESC, id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new CorrectionItem(
                reader.GetInt64(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                DateTime.Parse(reader.GetString(4)).ToUniversalTime()));
        }
        return items;
    }

    public async Task UpdateCorrectionAsync(long id, string spoken, string preferred)
    {
        spoken = spoken.Trim();
        preferred = preferred.Trim();
        if (spoken.Length == 0 || preferred.Length == 0) return;

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            UPDATE corrections
            SET spoken = $spoken, preferred = $preferred
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$spoken", spoken);
        command.Parameters.AddWithValue("$preferred", preferred);
        await command.ExecuteNonQueryAsync();
    }

    public async Task DeleteCorrectionAsync(long id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM corrections WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<MemoryItem>> GetPreferencesAsync(int limit = 50)
    {
        var items = new List<MemoryItem>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, content, created_utc
            FROM memory_items
            WHERE enabled = 1
            ORDER BY id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new MemoryItem(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                DateTime.Parse(reader.GetString(3)).ToUniversalTime()));
        }

        return items;
    }

    public async Task DeletePreferenceAsync(long id)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE memory_items SET enabled = 0 WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<MemoryContext> BuildContextAsync(
        string rawText,
        string appName,
        float[]? queryEmbedding = null)
    {
        var preferences = await GetPreferencesAsync(12);
        var corrections = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var recentExamples = new List<string>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = "SELECT spoken, preferred FROM corrections ORDER BY use_count DESC, id DESC LIMIT 200;";
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var spoken = reader.GetString(0);
            if (rawText.Contains(spoken, StringComparison.OrdinalIgnoreCase))
            {
                corrections[spoken] = reader.GetString(1);
            }
        }

        await reader.DisposeAsync();
        if (queryEmbedding is { Length: LocalEmbeddingService.Dimensions })
        {
            recentExamples.AddRange(await FindSimilarHistoryAsync(
                connection, queryEmbedding, appName, limit: 5, candidateLimit: 2000));
        }

        if (recentExamples.Count == 0)
        {
            var historyCommand = connection.CreateCommand();
            historyCommand.CommandText = """
                SELECT final_text
                FROM text_history
                WHERE length(final_text) > 0
                ORDER BY (app_name = $app) DESC, id DESC
                LIMIT 5;
                """;
            historyCommand.Parameters.AddWithValue("$app", appName);
            await using var historyReader = await historyCommand.ExecuteReaderAsync();
            while (await historyReader.ReadAsync())
            {
                recentExamples.Add(historyReader.GetString(0));
            }
        }

        return new MemoryContext
        {
            Preferences = preferences.Select(item => item.Content).ToArray(),
            RecentExamples = recentExamples,
            Corrections = corrections
        };
    }

    public async Task<long> AddHistoryAsync(string rawText, string finalText, string appName)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO text_history(raw_text, final_text, app_name, created_utc)
            VALUES ($raw, $final, $app, $created)
            RETURNING id;
            """;
        command.Parameters.AddWithValue("$raw", rawText);
        command.Parameters.AddWithValue("$final", finalText);
        command.Parameters.AddWithValue("$app", appName);
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    public async Task SaveEmbeddingAsync(long historyId, float[] embedding)
    {
        if (embedding.Length != LocalEmbeddingService.Dimensions)
        {
            throw new ArgumentException($"语义向量必须是 {LocalEmbeddingService.Dimensions} 维。", nameof(embedding));
        }

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO semantic_embeddings(history_id, model_id, dimensions, embedding, created_utc)
            VALUES ($historyId, $modelId, $dimensions, $embedding, $created)
            ON CONFLICT(history_id) DO UPDATE SET
                model_id = excluded.model_id,
                dimensions = excluded.dimensions,
                embedding = excluded.embedding,
                created_utc = excluded.created_utc;
            """;
        command.Parameters.AddWithValue("$historyId", historyId);
        command.Parameters.AddWithValue("$modelId", LocalEmbeddingService.ModelId);
        command.Parameters.AddWithValue("$dimensions", embedding.Length);
        command.Parameters.AddWithValue("$embedding", ToBytes(embedding));
        command.Parameters.AddWithValue("$created", DateTime.UtcNow.ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    public async Task<IReadOnlyList<UnembeddedHistoryItem>> GetUnembeddedHistoryAsync(int limit = 50)
    {
        var items = new List<UnembeddedHistoryItem>();
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT h.id, h.final_text, h.app_name, h.created_utc
            FROM text_history h
            LEFT JOIN semantic_embeddings e ON e.history_id = h.id
            WHERE e.history_id IS NULL AND length(h.final_text) > 0
            ORDER BY h.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$limit", limit);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            items.Add(new UnembeddedHistoryItem(
                reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
                DateTime.Parse(reader.GetString(3)).ToUniversalTime()));
        }
        return items;
    }

    private static async Task<IReadOnlyList<string>> FindSimilarHistoryAsync(
        SqliteConnection connection,
        float[] query,
        string appName,
        int limit,
        int candidateLimit)
    {
        var candidates = new List<(string Text, string AppName, DateTime CreatedUtc, double Score)>();
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT h.final_text, h.app_name, h.created_utc, e.embedding
            FROM semantic_embeddings e
            JOIN text_history h ON h.id = e.history_id
            WHERE e.model_id = $modelId AND e.dimensions = $dimensions
            ORDER BY h.id DESC
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$modelId", LocalEmbeddingService.ModelId);
        command.Parameters.AddWithValue("$dimensions", LocalEmbeddingService.Dimensions);
        command.Parameters.AddWithValue("$limit", candidateLimit);

        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var vector = FromBytes((byte[])reader[3]);
            if (vector.Length != query.Length) continue;
            var similarity = Dot(query, vector);
            if (similarity < 0.80) continue;

            var candidateApp = reader.GetString(1);
            var sameAppBonus = string.Equals(candidateApp, appName, StringComparison.OrdinalIgnoreCase) ? 0.035 : 0;
            var created = DateTime.Parse(reader.GetString(2)).ToUniversalTime();
            var ageDays = Math.Max(0, (DateTime.UtcNow - created).TotalDays);
            var recencyBonus = Math.Max(0, 0.02 * (1 - ageDays / 90));
            candidates.Add((reader.GetString(0), candidateApp, created, similarity + sameAppBonus + recencyBonus));
        }

        return candidates
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.CreatedUtc)
            .Take(limit)
            .Select(item => item.Text)
            .ToArray();
    }

    private static async Task PruneHistoryAsync(SqliteConnection connection)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            DELETE FROM semantic_embeddings
            WHERE history_id IN (
                SELECT id FROM text_history
                WHERE created_utc < $cutoff
                   OR id NOT IN (SELECT id FROM text_history ORDER BY id DESC LIMIT 5000)
            );
            DELETE FROM text_history
            WHERE created_utc < $cutoff
               OR id NOT IN (SELECT id FROM text_history ORDER BY id DESC LIMIT 5000);
            """;
        command.Parameters.AddWithValue("$cutoff", DateTime.UtcNow.AddDays(-90).ToString("O"));
        await command.ExecuteNonQueryAsync();
    }

    private static byte[] ToBytes(float[] vector)
    {
        var bytes = new byte[vector.Length * sizeof(float)];
        Buffer.BlockCopy(vector, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    private static float[] FromBytes(byte[] bytes)
    {
        if (bytes.Length % sizeof(float) != 0) return [];
        var vector = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, vector, 0, bytes.Length);
        return vector;
    }

    private static double Dot(float[] left, float[] right)
    {
        double sum = 0;
        for (var index = 0; index < left.Length; index++) sum += left[index] * right[index];
        return sum;
    }
}
