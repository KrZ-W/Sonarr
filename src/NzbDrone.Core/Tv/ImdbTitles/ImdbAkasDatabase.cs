using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.Globalization;
using System.IO;
using NLog;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;

namespace NzbDrone.Core.Tv.ImdbTitles
{
    // krzw(imdb-title-provider): the local SQLite index of kept akas rows, <AppData>/imdb-akas.db.
    // Separate from radarr.db on purpose: it is a disposable cache rebuilt from the dump, never
    // migrated, never backed up. Build() writes a sibling .tmp file and renames it over the live
    // file only once every row is in, so a failed refresh leaves the previous index untouched.
    public interface IImdbAkasDatabase
    {
        string Path { get; }
        bool Exists { get; }
        ImdbAkasDatabaseInfo GetInfo();
        long Build(IEnumerable<ImdbAkasRow> rows, ImdbAkasDatabaseInfo info);
        List<ImdbAkasRow> GetTitles(string tconst);
    }

    public class ImdbAkasDatabaseInfo
    {
        public string ETag { get; set; }
        public string LastModified { get; set; }
        public string FilterSignature { get; set; }
        public DateTime? BuiltAt { get; set; }
        public long RowCount { get; set; }
    }

    public class ImdbAkasDatabase : IImdbAkasDatabase
    {
        public const string FileName = "imdb-akas.db";

        private const int BatchSize = 20000;

        private readonly Logger _logger;

        public ImdbAkasDatabase(IAppFolderInfo appFolderInfo, Logger logger)
        {
            Path = System.IO.Path.Combine(appFolderInfo.GetAppDataPath(), FileName);
            _logger = logger;
        }

        public string Path { get; }

        public bool Exists => File.Exists(Path);

        public ImdbAkasDatabaseInfo GetInfo()
        {
            if (!Exists)
            {
                return null;
            }

            try
            {
                using var connection = Open(Path, readOnly: true);
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT key, value FROM meta";

                var values = new Dictionary<string, string>();

                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        values[reader.GetString(0)] = reader.IsDBNull(1) ? null : reader.GetString(1);
                    }
                }

                return new ImdbAkasDatabaseInfo
                {
                    ETag = values.GetValueOrDefault("etag"),
                    LastModified = values.GetValueOrDefault("lastModified"),
                    FilterSignature = values.GetValueOrDefault("filter"),
                    BuiltAt = ParseDate(values.GetValueOrDefault("builtAt")),
                    RowCount = long.TryParse(values.GetValueOrDefault("rowCount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var count) ? count : 0
                };
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "IMDb title index {0} is unreadable; it will be rebuilt on the next refresh", Path);
                return null;
            }
        }

        public long Build(IEnumerable<ImdbAkasRow> rows, ImdbAkasDatabaseInfo info)
        {
            var tempPath = Path + ".tmp";
            var directory = System.IO.Path.GetDirectoryName(Path);

            if (directory.IsNotNullOrWhiteSpace() && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            DeleteIfExists(tempPath);

            long count = 0;

            try
            {
                using (var connection = Open(tempPath, readOnly: false))
                {
                    Execute(connection, "PRAGMA journal_mode=OFF");
                    Execute(connection, "PRAGMA synchronous=OFF");
                    Execute(connection, "CREATE TABLE titles (tconst TEXT NOT NULL, title TEXT NOT NULL, region TEXT, language TEXT, attributes TEXT, isOriginal INTEGER NOT NULL)");
                    Execute(connection, "CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT)");

                    var transaction = connection.BeginTransaction();

                    using (var insert = connection.CreateCommand())
                    {
                        insert.CommandText = "INSERT INTO titles (tconst, title, region, language, attributes, isOriginal) VALUES (@tconst, @title, @region, @language, @attributes, @isOriginal)";
                        var tconst = insert.Parameters.Add("@tconst", System.Data.DbType.String);
                        var title = insert.Parameters.Add("@title", System.Data.DbType.String);
                        var region = insert.Parameters.Add("@region", System.Data.DbType.String);
                        var language = insert.Parameters.Add("@language", System.Data.DbType.String);
                        var attributes = insert.Parameters.Add("@attributes", System.Data.DbType.String);
                        var isOriginal = insert.Parameters.Add("@isOriginal", System.Data.DbType.Int32);

                        foreach (var row in rows)
                        {
                            tconst.Value = row.Tconst;
                            title.Value = row.Title;
                            region.Value = (object)row.Region ?? DBNull.Value;
                            language.Value = (object)row.Language ?? DBNull.Value;
                            attributes.Value = (object)row.Attributes ?? DBNull.Value;
                            isOriginal.Value = row.IsOriginal ? 1 : 0;
                            insert.ExecuteNonQuery();
                            count++;

                            if (count % BatchSize == 0)
                            {
                                transaction.Commit();
                                transaction.Dispose();
                                transaction = connection.BeginTransaction();
                            }
                        }
                    }

                    transaction.Commit();
                    transaction.Dispose();

                    Execute(connection, "CREATE INDEX idx_titles_tconst ON titles (tconst)");

                    WriteMeta(connection, "etag", info?.ETag);
                    WriteMeta(connection, "lastModified", info?.LastModified);
                    WriteMeta(connection, "filter", info?.FilterSignature);
                    WriteMeta(connection, "builtAt", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                    WriteMeta(connection, "rowCount", count.ToString(CultureInfo.InvariantCulture));
                }

                // rename(2) over the live file: readers see either the old or the new index, never a
                // half-written one.
                File.Move(tempPath, Path, true);
            }
            catch
            {
                DeleteIfExists(tempPath);
                throw;
            }

            return count;
        }

        public List<ImdbAkasRow> GetTitles(string tconst)
        {
            var result = new List<ImdbAkasRow>();

            if (tconst.IsNullOrWhiteSpace() || !Exists)
            {
                return result;
            }

            using var connection = Open(Path, readOnly: true);
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT title, region, language, attributes, isOriginal FROM titles WHERE tconst = @tconst ORDER BY rowid";
            command.Parameters.AddWithValue("@tconst", tconst.Trim());

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                result.Add(new ImdbAkasRow
                {
                    Tconst = tconst,
                    Title = reader.GetString(0),
                    Region = reader.IsDBNull(1) ? null : reader.GetString(1),
                    Language = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Attributes = reader.IsDBNull(3) ? null : reader.GetString(3),
                    IsOriginal = !reader.IsDBNull(4) && reader.GetInt32(4) == 1
                });
            }

            return result;
        }

        private static SQLiteConnection Open(string path, bool readOnly)
        {
            var builder = new SQLiteConnectionStringBuilder
            {
                DataSource = path,
                Version = 3,
                Pooling = false,
                ReadOnly = readOnly,
                FailIfMissing = readOnly
            };

            var connection = new SQLiteConnection(builder.ConnectionString);
            connection.Open();

            return connection;
        }

        private static void Execute(SQLiteConnection connection, string sql)
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        private static void WriteMeta(SQLiteConnection connection, string key, string value)
        {
            using var command = connection.CreateCommand();
            command.CommandText = "INSERT OR REPLACE INTO meta (key, value) VALUES (@key, @value)";
            command.Parameters.AddWithValue("@key", key);
            command.Parameters.AddWithValue("@value", (object)value ?? DBNull.Value);
            command.ExecuteNonQuery();
        }

        private static DateTime? ParseDate(string value)
        {
            if (value.IsNullOrWhiteSpace())
            {
                return null;
            }

            return DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date) ? date.ToUniversalTime() : null;
        }

        private static void DeleteIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
