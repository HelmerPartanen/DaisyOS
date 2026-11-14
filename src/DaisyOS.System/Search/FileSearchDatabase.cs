using DaisyOS.Core.Search;
using Microsoft.Data.Sqlite;

namespace DaisyOS.System.Search;

/// <summary>One serialized write connection, independent bounded read connections, WAL and FTS5.</summary>
public sealed class FileSearchDatabase(SearchIndexOptions options) : IDisposable
{
    private SqliteConnection? _writer;
    private readonly object _gate = new();
    private string ConnectionString => new SqliteConnectionStringBuilder
    { DataSource = options.DatabasePath, Pooling = false, ForeignKeys = true, DefaultTimeout = 1 }.ToString();
    public void Initialize()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("The search index is Linux-only.");
        lock (_gate)
        {
            if (_writer is not null) return;
            var directory = Path.GetDirectoryName(Path.GetFullPath(options.DatabasePath))!;
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using (var safe = SearchFileAccess.Open(directory))
                if (safe is null) throw new IOException("The search database directory is inaccessible or contains a symbolic link.");
            File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            if (File.Exists(options.DatabasePath) && (File.GetAttributes(options.DatabasePath) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("The search database must not be a symbolic link.");
            var connection = new SqliteConnection(ConnectionString);
            try
            {
                connection.Open();
                using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA quick_check;";
                if (command.ExecuteScalar()?.ToString() != "ok") throw new InvalidDataException("Search database integrity check failed.");
                command.CommandText = "PRAGMA user_version;";
                var version = Convert.ToInt32(command.ExecuteScalar());
                if (version > 1) throw new InvalidOperationException("The search database uses a newer schema.");
                command.CommandText = "PRAGMA journal_mode=WAL; PRAGMA synchronous=NORMAL; PRAGMA busy_timeout=1000;";
                command.ExecuteNonQuery();
                using var transaction = connection.BeginTransaction();
                command.Transaction = transaction;
                command.CommandText = """
                    CREATE TABLE IF NOT EXISTS Files(
                      Id INTEGER PRIMARY KEY, Path TEXT NOT NULL UNIQUE, Name TEXT NOT NULL, Parent TEXT NOT NULL,
                      Extension TEXT NOT NULL, IsDirectory INTEGER NOT NULL, Size INTEGER NOT NULL, Modified INTEGER NOT NULL,
                      Root TEXT NOT NULL, Scan TEXT NOT NULL);
                    CREATE INDEX IF NOT EXISTS Files_Root ON Files(Root,Scan);
                    CREATE INDEX IF NOT EXISTS Files_Extension ON Files(Extension);
                    CREATE INDEX IF NOT EXISTS Files_Name ON Files(Name COLLATE NOCASE);
                    CREATE INDEX IF NOT EXISTS Files_Parent ON Files(Parent);
                    CREATE INDEX IF NOT EXISTS Files_Size ON Files(Size);
                    CREATE VIRTUAL TABLE IF NOT EXISTS FileNames USING fts5(Name, content='Files', content_rowid='Id',
                      tokenize='unicode61 remove_diacritics 2', prefix='2 3 4');
                    CREATE TRIGGER IF NOT EXISTS Files_ai AFTER INSERT ON Files BEGIN
                      INSERT INTO FileNames(rowid,Name) VALUES(new.Id,new.Name); END;
                    CREATE TRIGGER IF NOT EXISTS Files_ad AFTER DELETE ON Files BEGIN
                      INSERT INTO FileNames(FileNames,rowid,Name) VALUES('delete',old.Id,old.Name); END;
                    CREATE TRIGGER IF NOT EXISTS Files_au AFTER UPDATE OF Name ON Files WHEN old.Name<>new.Name BEGIN
                      INSERT INTO FileNames(FileNames,rowid,Name) VALUES('delete',old.Id,old.Name);
                      INSERT INTO FileNames(rowid,Name) VALUES(new.Id,new.Name); END;
                    CREATE TABLE IF NOT EXISTS State(Key TEXT PRIMARY KEY,Value TEXT NOT NULL);
                    CREATE TABLE IF NOT EXISTS UsageEvents(Path TEXT PRIMARY KEY REFERENCES Files(Path) ON DELETE CASCADE,
                      UseCount INTEGER NOT NULL, LastUsed INTEGER NOT NULL);
                    CREATE INDEX IF NOT EXISTS Usage_LastUsed ON UsageEvents(LastUsed DESC);
                    PRAGMA user_version=1;
                    """;
                command.ExecuteNonQuery();
                transaction.Commit();
                File.SetUnixFileMode(options.DatabasePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                _writer = connection;
            }
            catch { connection.Dispose(); throw; }
        }
    }

    public void Upsert(IReadOnlyList<IndexedFile> files, string root, string scan, CancellationToken token)
    {
        lock (_gate)
        {
            using var transaction = Writer.BeginTransaction();
            using var command = Writer.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO Files(Path,Name,Parent,Extension,IsDirectory,Size,Modified,Root,Scan)
                VALUES($path,$name,$parent,$extension,$directory,$size,$modified,$root,$scan)
                ON CONFLICT(Path) DO UPDATE SET Name=excluded.Name,Parent=excluded.Parent,Extension=excluded.Extension,
                  IsDirectory=excluded.IsDirectory,Size=excluded.Size,Modified=excluded.Modified,Root=excluded.Root,Scan=excluded.Scan;
                """;
            foreach (var parameter in new[] { "$path", "$name", "$parent", "$extension", "$directory", "$size", "$modified", "$root", "$scan" })
                command.Parameters.Add(new SqliteParameter(parameter, ""));
            command.Prepare();
            foreach (var file in files)
            {
                token.ThrowIfCancellationRequested();
                command.Parameters[0].Value = file.Path; command.Parameters[1].Value = file.Name;
                command.Parameters[2].Value = file.Parent; command.Parameters[3].Value = file.Extension;
                command.Parameters[4].Value = file.IsDirectory; command.Parameters[5].Value = file.Size;
                command.Parameters[6].Value = file.ModifiedUtcTicks; command.Parameters[7].Value = root; command.Parameters[8].Value = scan;
                command.ExecuteNonQuery();
            }
            transaction.Commit();
        }
    }
    private SqliteConnection Writer => _writer ?? throw new InvalidOperationException("Search index is not initialized.");
    public void RemoveSubtree(string path)
    {
        lock (_gate)
        {
            using var command = Writer.CreateCommand();
            command.CommandText = "DELETE FROM Files WHERE Path=$path OR substr(Path,1,length($prefix))=$prefix";
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$prefix", path.TrimEnd('/') + "/");
            command.ExecuteNonQuery();
        }
    }
    public void FinishScan(string root, string scan)
    {
        lock (_gate)
        {
            using var command = Writer.CreateCommand();
            command.CommandText = "DELETE FROM Files WHERE Root=$root AND Scan<>$scan; INSERT OR REPLACE INTO State VALUES('last-reconciliation',$time);";
            command.Parameters.AddWithValue("$root", root); command.Parameters.AddWithValue("$scan", scan);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.ToString("O"));
            command.ExecuteNonQuery();
        }
    }
    public long Count
    {
        get { lock (_gate) { using var command = Writer.CreateCommand(); command.CommandText = "SELECT count(*) FROM Files"; return (long)command.ExecuteScalar()!; } }
    }
    public IReadOnlyList<IndexedFile> Search(SearchQuery query, CancellationToken token)
    {
        if (query.Tokens.Count == 0 && !query.FileFilters.IsActive) return [];
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        using var cancellation = token.Register(command.Cancel);
        var quotedTerms = query.Tokens.Take(12).Select(t => "\"" + t.Replace("\"", "\"\"") + "\"").ToArray();
        var expression = string.Join(" AND ", quotedTerms);
        var filters = query.FileFilters;
        var hasTerms = query.Tokens.Count > 0;
        const string columns = "SELECT f.Path,f.Name,f.Parent,f.Extension,f.IsDirectory,f.Size,f.Modified FROM Files f ";
        // CROSS JOIN keeps FTS as the outer cursor. Starting with a common extension
        // reinitializes the virtual-table search for every candidate row.
        const string ftsColumns = "SELECT f.Path,f.Name,f.Parent,f.Extension,f.IsDirectory,f.Size,f.Modified FROM FileNames CROSS JOIN Files f ON f.Id=FileNames.rowid ";
        var conditions = "";
        if (filters.Extension is not null) conditions += " AND f.Extension=$extension ";
        if (filters.IsDirectory is not null) conditions += " AND f.IsDirectory=$directory ";
        if (filters.MinimumSize is not null) conditions += " AND f.Size>$minimum ";
        if (filters.MaximumSize is not null) conditions += " AND f.Size<$maximum ";
        if (filters.Location is not null)
            conditions += " AND instr('/' || ltrim(f.Parent,'/') || '/', '/' || ltrim($location,'/') || '/')>0 ";
        // First retrieve exact names through a B-tree, then a bounded FTS candidate set.
        // Ranking the entire posting list for a common prefix would sort millions of rows.
        if (hasTerms)
        {
            command.Parameters.AddWithValue("$query", expression);
            command.Parameters.AddWithValue("$exact", query.RawText.Trim());
            command.Parameters.AddWithValue("$prefix", query.RawText.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
        }
        command.Parameters.AddWithValue("$extension", (object?)filters.Extension ?? DBNull.Value);
        command.Parameters.AddWithValue("$directory", (object?)filters.IsDirectory ?? DBNull.Value);
        command.Parameters.AddWithValue("$minimum", (object?)filters.MinimumSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$maximum", (object?)filters.MaximumSize ?? DBNull.Value);
        command.Parameters.AddWithValue("$location", (object?)filters.Location ?? DBNull.Value);
        List<IndexedFile> Retrieve(string sql)
        {
            command.CommandText = sql;
            using var reader = command.ExecuteReader();
            var files = new List<IndexedFile>();
            while (reader.Read())
            {
                token.ThrowIfCancellationRequested();
                files.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                    reader.GetBoolean(4), reader.GetInt64(5), reader.GetInt64(6)));
            }
            return files;
        }
        if (hasTerms)
        {
            // Raw text contains filter operators; it cannot equal a filename in that case.
            // Avoid letting a broad extension index drive these guaranteed misses.
            if (!filters.IsActive)
            {
                var exact = Retrieve(columns + " INDEXED BY Files_Name WHERE f.Name=$exact COLLATE NOCASE LIMIT 50;");
                var prefix = Retrieve(columns + " INDEXED BY Files_Name WHERE f.Name LIKE $prefix ESCAPE '\\' LIMIT 200;");
                if (exact.Count > 0 || prefix.Count > 0) return exact.Concat(prefix).DistinctBy(file => file.Path, StringComparer.Ordinal).Take(250).ToArray();
            }
            var exactTerms = Retrieve(ftsColumns + " WHERE FileNames MATCH $query " + conditions + " LIMIT 200;");
            if (exactTerms.Count > 0) return exactTerms;
            command.Parameters["$query"].Value = string.Join(" AND ", quotedTerms.Select((term, i) => term + (i == quotedTerms.Length - 1 ? "*" : "")));
            var wordPrefix = Retrieve(ftsColumns + " WHERE FileNames MATCH $query " + conditions + " LIMIT 200;");
            if (wordPrefix.Count > 0) return wordPrefix;
            command.Parameters["$query"].Value = string.Join(" AND ", quotedTerms.Select(term => term + "*"));
            return Retrieve(ftsColumns + " WHERE FileNames MATCH $query " + conditions + " LIMIT 200;");
        }
        return Retrieve(columns + " WHERE 1=1 " + conditions + " LIMIT 200;");
    }
    public void RecordUse(string path)
    {
        lock (_gate)
        {
            using var command = Writer.CreateCommand();
            command.CommandText = """
                INSERT INTO UsageEvents(Path,UseCount,LastUsed) SELECT Path,1,$time FROM Files WHERE Path=$path
                ON CONFLICT(Path) DO UPDATE SET UseCount=UseCount+1,LastUsed=excluded.LastUsed;
                """;
            command.Parameters.AddWithValue("$path", path);
            command.Parameters.AddWithValue("$time", DateTimeOffset.UtcNow.UtcTicks);
            command.ExecuteNonQuery();
        }
    }
    public IReadOnlyList<IndexedFile> Recent(CancellationToken token)
    {
        using var connection = new SqliteConnection(ConnectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        using var cancellation = token.Register(command.Cancel);
        command.CommandText = """
            SELECT f.Path,f.Name,f.Parent,f.Extension,f.IsDirectory,f.Size,f.Modified FROM UsageEvents u
            JOIN Files f ON f.Path=u.Path ORDER BY u.LastUsed DESC LIMIT 200;
            """;
        using var reader = command.ExecuteReader();
        var files = new List<IndexedFile>();
        while (reader.Read())
        {
            token.ThrowIfCancellationRequested();
            files.Add(new(reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3),
                reader.GetBoolean(4), reader.GetInt64(5), reader.GetInt64(6)));
        }
        return files;
    }
    public void Dispose() { lock (_gate) { _writer?.Dispose(); _writer = null; } }
}
