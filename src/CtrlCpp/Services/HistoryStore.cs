using System.IO;
using System.Text.Json;
using CtrlCpp.Models;
using Microsoft.Data.Sqlite;

namespace CtrlCpp.Services;

/// <summary>SQLite 历史存储。单连接 + 信号量串行化（Microsoft.Data.Sqlite 连接非线程安全）。</summary>
public sealed class HistoryStore : IDisposable
{
    private SqliteConnection? _conn;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public void Open(string dbPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(dbPath)!);
        _conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        }.ToString());
        _conn.Open();
        Exec("PRAGMA journal_mode=WAL");
        Exec("PRAGMA busy_timeout=5000");
        Exec("""
            CREATE TABLE IF NOT EXISTS Items (
              Id           INTEGER PRIMARY KEY AUTOINCREMENT,
              Kind         INTEGER NOT NULL,
              ContentHash  TEXT NOT NULL,
              Preview      TEXT,
              FullText     TEXT,
              MediaPath    TEXT,
              FileList     TEXT,
              Width        INTEGER,
              Height       INTEGER,
              ByteSize     INTEGER NOT NULL,
              SourceApp    TEXT,
              FirstSeenAt  INTEGER NOT NULL,
              LastSeenAt   INTEGER NOT NULL
            )
            """);
        Exec("CREATE UNIQUE INDEX IF NOT EXISTS idx_items_hash ON Items(ContentHash)");
        Exec("CREATE INDEX IF NOT EXISTS idx_items_lastseen ON Items(LastSeenAt DESC)");
    }

    private void Exec(string sql)
    {
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = sql;
        _ = cmd.ExecuteNonQuery();
    }

    public long Upsert(ClipboardPayload p, string? mediaRel)
    {
        _gate.Wait();
        try
        {
            var now = p.LastSeenAtOverride != 0 ? p.LastSeenAtOverride : DateTime.UtcNow.Ticks;
            using (var cmd = _conn!.CreateCommand())
            {
                cmd.CommandText = """
                    INSERT INTO Items (Kind, ContentHash, Preview, FullText, MediaPath, FileList, Width, Height, ByteSize, SourceApp, FirstSeenAt, LastSeenAt)
                    VALUES ($k, $h, $pv, $ft, $mp, $fl, $w, $hh, $sz, $src, $now, $now)
                    ON CONFLICT(ContentHash) DO UPDATE SET
                      LastSeenAt = excluded.LastSeenAt,
                      SourceApp = excluded.SourceApp
                    """;
                cmd.Parameters.AddWithValue("$k", (long)p.Kind);
                cmd.Parameters.AddWithValue("$h", p.ContentHash);
                cmd.Parameters.AddWithValue("$pv", p.Preview);
                cmd.Parameters.AddWithValue("$ft", (object?)p.FullText ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$mp", (object?)mediaRel ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$fl", (object?)p.FileListJson ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$w", p.Width);
                cmd.Parameters.AddWithValue("$hh", p.Height);
                cmd.Parameters.AddWithValue("$sz", p.ByteSize);
                cmd.Parameters.AddWithValue("$src", (object?)p.SourceApp ?? DBNull.Value);
                cmd.Parameters.AddWithValue("$now", now);
                _ = cmd.ExecuteNonQuery();
            }
            using var idCmd = _conn.CreateCommand();
            idCmd.CommandText = "SELECT Id FROM Items WHERE ContentHash = $h";
            idCmd.Parameters.AddWithValue("$h", p.ContentHash);
            return Convert.ToInt64(idCmd.ExecuteScalar());
        }
        finally { _gate.Release(); }
    }

    public List<ClipboardItem> ListRecent(int limit)
    {
        _gate.Wait();
        try
        {
            var list = new List<ClipboardItem>();
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = """
                SELECT Id, Kind, ContentHash, Preview, FullText, MediaPath, FileList, Width, Height, ByteSize, SourceApp, FirstSeenAt, LastSeenAt
                FROM Items ORDER BY LastSeenAt DESC LIMIT $n
                """;
            cmd.Parameters.AddWithValue("$n", limit);
            using var r = cmd.ExecuteReader();
            while (r.Read()) list.Add(MapRow(r));
            return list;
        }
        finally { _gate.Release(); }
    }

    private static ClipboardItem MapRow(SqliteDataReader r)
    {
        return new ClipboardItem
        {
            Id = r.GetInt64(0),
            Kind = (ItemKind)r.GetInt64(1),
            ContentHash = r.GetString(2),
            Preview = r.IsDBNull(3) ? "" : r.GetString(3),
            FullText = r.IsDBNull(4) ? null : r.GetString(4),
            MediaPath = r.IsDBNull(5) ? null : r.GetString(5),
            FileListJson = r.IsDBNull(6) ? null : r.GetString(6),
            Width = r.IsDBNull(7) ? 0 : r.GetInt32(7),
            Height = r.IsDBNull(8) ? 0 : r.GetInt32(8),
            ByteSize = r.GetInt64(9),
            SourceApp = r.IsDBNull(10) ? null : r.GetString(10),
            FirstSeenAt = r.GetInt64(11),
            LastSeenAt = r.GetInt64(12),
        };
    }

    /// <summary>删除指定 id 的记录，返回需要删除的媒体相对路径。</summary>
    public List<string> DeleteIds(IEnumerable<long> ids)
    {
        _gate.Wait();
        try
        {
            var media = new List<string>();
            foreach (var id in ids)
            {
                using (var sel = _conn!.CreateCommand())
                {
                    sel.CommandText = "SELECT MediaPath FROM Items WHERE Id = $id AND MediaPath IS NOT NULL";
                    sel.Parameters.AddWithValue("$id", id);
                    var result = sel.ExecuteScalar();
                    if (result != null && result != DBNull.Value) media.Add((string)result);
                }
                using var del = _conn.CreateCommand();
                del.CommandText = "DELETE FROM Items WHERE Id = $id";
                del.Parameters.AddWithValue("$id", id);
                _ = del.ExecuteNonQuery();
            }
            return media;
        }
        finally { _gate.Release(); }
    }

    /// <summary>删除早于 cutoffTicks 的记录，返回需删除的媒体路径。</summary>
    public List<string> DeleteOlderThan(long cutoffTicks)
    {
        _gate.Wait();
        try
        {
            var media = new List<string>();
            if (_conn == null) return media;
            using (var sel = _conn!.CreateCommand())
            {
                sel.CommandText = "SELECT MediaPath FROM Items WHERE LastSeenAt < $c AND MediaPath IS NOT NULL";
                sel.Parameters.AddWithValue("$c", cutoffTicks);
                using var r = sel.ExecuteReader();
                while (r.Read()) media.Add(r.GetString(0));
            }
            using var del = _conn.CreateCommand();
            del.CommandText = "DELETE FROM Items WHERE LastSeenAt < $c";
            del.Parameters.AddWithValue("$c", cutoffTicks);
            _ = del.ExecuteNonQuery();
            return media;
        }
        finally { _gate.Release(); }
    }

    public long SumSize()
    {
        _gate.Wait();
        try { return SumSizeInner(); }
        finally { _gate.Release(); }
    }

    private long SumSizeInner()
    {
        if (_conn == null) return 0;
        using var cmd = _conn!.CreateCommand();
        cmd.CommandText = "SELECT COALESCE(SUM(ByteSize), 0) FROM Items";
        return Convert.ToInt64(cmd.ExecuteScalar());
    }

    /// <summary>
    /// 若缓存超过 maxBytes，删除最旧的一批（20 条）记录并返回其媒体路径；
    /// 未超限返回 null。
    /// </summary>
    public List<string>? EvictOldest(long maxBytes)
    {
        _gate.Wait();
        try
        {
            if (SumSizeInner() <= maxBytes) return null;
            var ids = new List<long>();
            var media = new List<string>();
            using (var sel = _conn!.CreateCommand())
            {
                sel.CommandText = "SELECT Id, MediaPath FROM Items ORDER BY LastSeenAt ASC LIMIT 20";
                using var r = sel.ExecuteReader();
                while (r.Read())
                {
                    ids.Add(r.GetInt64(0));
                    if (!r.IsDBNull(1)) media.Add(r.GetString(1));
                }
            }
            if (ids.Count == 0) return media;
            using var del = _conn.CreateCommand();
            del.CommandText = "DELETE FROM Items WHERE Id IN (" + string.Join(",", ids) + ")";
            _ = del.ExecuteNonQuery();
            return media;
        }
        finally { _gate.Release(); }
    }

    /// <summary>清空全部记录，返回需删除的媒体路径。</summary>
    public List<string> ClearAll()
    {
        _gate.Wait();
        try
        {
            var media = new List<string>();
            using (var sel = _conn!.CreateCommand())
            {
                sel.CommandText = "SELECT MediaPath FROM Items WHERE MediaPath IS NOT NULL";
                using var r = sel.ExecuteReader();
                while (r.Read()) media.Add(r.GetString(0));
            }
            using var del = _conn.CreateCommand();
            del.CommandText = "DELETE FROM Items";
            _ = del.ExecuteNonQuery();
            return media;
        }
        finally { _gate.Release(); }
    }

    public List<string> GetAllMediaPaths()
    {
        _gate.Wait();
        try
        {
            var media = new List<string>();
            using var cmd = _conn!.CreateCommand();
            cmd.CommandText = "SELECT MediaPath FROM Items WHERE MediaPath IS NOT NULL";
            using var r = cmd.ExecuteReader();
            while (r.Read()) media.Add(r.GetString(0));
            return media;
        }
        finally { _gate.Release(); }
    }

    public void Close()
    {
        _gate.Wait();
        try
        {
            _conn?.Close();
            _conn?.Dispose();
            _conn = null;
        }
        finally { _gate.Release(); }
    }

    public void Dispose() => Close();
}
