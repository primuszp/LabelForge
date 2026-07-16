using Microsoft.Data.Sqlite;
using LabelForge.Core;

namespace LabelForge.Persistence;

public sealed record DatasetIndexImage(
    long ImageId, string FilePath, string FileName, string Month, bool HasAnnotations,
    AnnotationWorkflowStatus Status, DatasetSplit Split, ImageQualityStatus Quality);

public sealed record DatasetIndexFilter(
    string? Search = null, string? Month = null, AnnotationWorkflowStatus? Status = null,
    DatasetSplit? Split = null, ImageQualityStatus? Quality = null);

public sealed class DatasetIndex : IAsyncDisposable
{
    private readonly SqliteConnection connection;

    private DatasetIndex(SqliteConnection connection) => this.connection = connection;

    public static async Task<DatasetIndex> OpenAsync(string path, CancellationToken cancellationToken = default)
    {
        var connection = new SqliteConnection($"Data Source={path};Cache=Shared;Pooling=False");
        await connection.OpenAsync(cancellationToken);
        var index = new DatasetIndex(connection);
        await index.CreateSchemaAsync(cancellationToken);
        return index;
    }

    public async Task RebuildAsync(IEnumerable<CocoDatasetImage> images, CancellationToken cancellationToken = default)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync("DELETE FROM images", cancellationToken, transaction);
        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO images(image_id,file_path,file_name,capture_date,month,has_annotations,status,split,quality)
            VALUES($id,$path,$name,$date,$month,$annotations,$status,$split,$quality)
            """;
        var id = command.Parameters.Add("$id", SqliteType.Integer);
        var filePath = command.Parameters.Add("$path", SqliteType.Text);
        var fileName = command.Parameters.Add("$name", SqliteType.Text);
        var date = command.Parameters.Add("$date", SqliteType.Text);
        var month = command.Parameters.Add("$month", SqliteType.Text);
        var annotations = command.Parameters.Add("$annotations", SqliteType.Integer);
        var status = command.Parameters.Add("$status", SqliteType.Integer);
        var split = command.Parameters.Add("$split", SqliteType.Integer);
        var quality = command.Parameters.Add("$quality", SqliteType.Integer);
        foreach (var image in images)
        {
            id.Value = image.ImageId; filePath.Value = image.FilePath; fileName.Value = Path.GetFileName(image.FilePath);
            date.Value = image.CaptureDate?.ToString("O") ?? (object)DBNull.Value;
            month.Value = image.CaptureDate?.ToString("yyyy-MM") ?? "Ismeretlen dátum";
            annotations.Value = image.HasAnnotations ? 1 : 0; status.Value = (int)image.Status;
            split.Value = (int)image.Split; quality.Value = (int)image.Quality;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<DatasetIndexImage>> QueryAsync(DatasetIndexFilter filter, int offset, int limit,
        CancellationToken cancellationToken = default)
    {
        var conditions = new List<string>();
        var command = connection.CreateCommand();
        if (!string.IsNullOrWhiteSpace(filter.Search)) { conditions.Add("file_name LIKE $search"); command.Parameters.AddWithValue("$search", $"%{filter.Search}%"); }
        if (!string.IsNullOrWhiteSpace(filter.Month)) { conditions.Add("month=$month"); command.Parameters.AddWithValue("$month", filter.Month); }
        if (filter.Status is { } status) { conditions.Add("status=$status"); command.Parameters.AddWithValue("$status", (int)status); }
        if (filter.Split is { } split) { conditions.Add("split=$split"); command.Parameters.AddWithValue("$split", (int)split); }
        if (filter.Quality is { } quality) { conditions.Add("quality=$quality"); command.Parameters.AddWithValue("$quality", (int)quality); }
        command.CommandText = $"SELECT image_id,file_path,file_name,month,has_annotations,status,split,quality FROM images {(conditions.Count == 0 ? "" : "WHERE " + string.Join(" AND ", conditions))} ORDER BY month,file_name LIMIT $limit OFFSET $offset";
        command.Parameters.AddWithValue("$limit", limit); command.Parameters.AddWithValue("$offset", offset);
        var result = new List<DatasetIndexImage>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(
            reader.GetInt64(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetBoolean(4),
            (AnnotationWorkflowStatus)reader.GetInt32(5), (DatasetSplit)reader.GetInt32(6), (ImageQualityStatus)reader.GetInt32(7)));
        return result;
    }

    public async Task UpdateWorkflowAsync(long imageId, AnnotationWorkflowStatus status, string? reviewer,
        CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand();
        command.CommandText = "UPDATE images SET status=$status,reviewer=$reviewer,reviewed_at=$at,dirty=1 WHERE image_id=$id";
        command.Parameters.AddWithValue("$status", (int)status); command.Parameters.AddWithValue("$reviewer", (object?)reviewer ?? DBNull.Value);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O")); command.Parameters.AddWithValue("$id", imageId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RecordRevisionAsync(long imageId, Guid annotationId, int revision, string payload,
        CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO annotation_revisions(image_id,annotation_id,revision,created_at,payload_json) VALUES($image,$annotation,$revision,$at,$payload)";
        command.Parameters.AddWithValue("$image", imageId);
        command.Parameters.AddWithValue("$annotation", annotationId.ToString());
        command.Parameters.AddWithValue("$revision", revision);
        command.Parameters.AddWithValue("$at", DateTimeOffset.UtcNow.ToString("O"));
        command.Parameters.AddWithValue("$payload", payload);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetMonthsAsync(CancellationToken cancellationToken = default)
    {
        var command = connection.CreateCommand(); command.CommandText = "SELECT DISTINCT month FROM images ORDER BY month";
        var result = new List<string>(); await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) result.Add(reader.GetString(0)); return result;
    }

    private async Task CreateSchemaAsync(CancellationToken token)
    {
        await ExecuteAsync("""
            CREATE TABLE IF NOT EXISTS images(
              image_id INTEGER PRIMARY KEY,file_path TEXT NOT NULL,file_name TEXT NOT NULL,capture_date TEXT,month TEXT NOT NULL,
              has_annotations INTEGER NOT NULL,status INTEGER NOT NULL,split INTEGER NOT NULL,quality INTEGER NOT NULL,
              reviewer TEXT,reviewed_at TEXT,dirty INTEGER NOT NULL DEFAULT 0);
            CREATE INDEX IF NOT EXISTS ix_images_month ON images(month);
            CREATE INDEX IF NOT EXISTS ix_images_status ON images(status);
            CREATE INDEX IF NOT EXISTS ix_images_split ON images(split);
            CREATE INDEX IF NOT EXISTS ix_images_quality ON images(quality);
            CREATE INDEX IF NOT EXISTS ix_images_name ON images(file_name);
            CREATE TABLE IF NOT EXISTS jobs(id TEXT PRIMARY KEY,type TEXT NOT NULL,status TEXT NOT NULL,created_at TEXT NOT NULL,
              started_at TEXT,finished_at TEXT,progress REAL NOT NULL DEFAULT 0,settings_json TEXT,error TEXT);
            CREATE TABLE IF NOT EXISTS annotation_revisions(id INTEGER PRIMARY KEY AUTOINCREMENT,image_id INTEGER NOT NULL,
              annotation_id TEXT NOT NULL,revision INTEGER NOT NULL,created_at TEXT NOT NULL,payload_json TEXT NOT NULL);
            CREATE INDEX IF NOT EXISTS ix_revisions_annotation ON annotation_revisions(annotation_id,revision);
            """, token);
    }

    private async Task ExecuteAsync(string sql, CancellationToken token, System.Data.Common.DbTransaction? transaction = null)
    {
        var command = connection.CreateCommand(); command.CommandText = sql; command.Transaction = (SqliteTransaction?)transaction;
        await command.ExecuteNonQueryAsync(token);
    }

    public async ValueTask DisposeAsync() => await connection.DisposeAsync();
}
