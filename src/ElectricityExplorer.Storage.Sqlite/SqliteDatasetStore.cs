using ElectricityExplorer.Core.Models;
using ElectricityExplorer.Core.Storage;
using Microsoft.Data.Sqlite;

namespace ElectricityExplorer.Storage.Sqlite;

public sealed class SqliteDatasetStore : IDatasetStore, IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _initialized;

    static SqliteDatasetStore()
    {
        SQLitePCL.Batteries_V2.Init();
    }

    public SqliteDatasetStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        DatabasePath = fullPath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true
        }.ToString();
    }

    public string DatabasePath { get; }

    public Task<IReadOnlyList<DatasetSummary>> GetSummariesAsync() =>
        ExecuteAsync(GetSummaries);

    public Task<ElectricityDataset?> GetAsync(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return ExecuteAsync(() => Get(id));
    }

    public Task SaveAsync(ElectricityDataset dataset)
    {
        ArgumentNullException.ThrowIfNull(dataset);
        return ExecuteAsync(() => Save(dataset));
    }

    public Task DeleteAsync(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        return ExecuteAsync(() => Delete(id));
    }

    public Task<DatasetStorageStatus> GetStatusAsync() =>
        Task.FromResult(
            new DatasetStorageStatus(
                "SQLite database on this PC",
                true,
                "NEM12 data and calculations remain in a local SQLite database on this PC. No data is sent to a server."));

    public void Dispose()
    {
        _gate.Dispose();
        SqliteConnection.ClearAllPools();
    }

    private async Task<T> ExecuteAsync<T>(Func<T> action)
    {
        await _gate.WaitAsync();
        try
        {
            return await Task.Run(action);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ExecuteAsync(Action action)
    {
        await _gate.WaitAsync();
        try
        {
            await Task.Run(action);
        }
        finally
        {
            _gate.Release();
        }
    }

    private IReadOnlyList<DatasetSummary> GetSummaries()
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                d.Id,
                d.Name,
                d.SourceFileName,
                d.ImportedAtUtcTicks,
                (SELECT COUNT(*) FROM Channels c WHERE c.DatasetId = d.Id),
                (SELECT COUNT(*) FROM Readings r WHERE r.DatasetId = d.Id),
                (SELECT MIN(TimestampTicks) FROM Readings r WHERE r.DatasetId = d.Id),
                (SELECT MAX(TimestampTicks) FROM Readings r WHERE r.DatasetId = d.Id)
            FROM Datasets d
            ORDER BY d.ImportedAtUtcTicks DESC;
            """;

        var summaries = new List<DatasetSummary>();
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                summaries.Add(new DatasetSummary
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    SourceFileName = reader.GetString(2),
                    ImportedAt = new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero),
                    ChannelCount = checked((int)reader.GetInt64(4)),
                    ReadingCount = checked((int)reader.GetInt64(5)),
                    Start = reader.IsDBNull(6)
                        ? null
                        : new DateTime(reader.GetInt64(6), DateTimeKind.Unspecified),
                    End = reader.IsDBNull(7)
                        ? null
                        : new DateTime(reader.GetInt64(7), DateTimeKind.Unspecified)
                });
            }
        }

        using var nmiCommand = connection.CreateCommand();
        nmiCommand.CommandText =
            """
            SELECT DISTINCT Nmi
            FROM Channels
            WHERE DatasetId = $datasetId
            ORDER BY Nmi;
            """;
        var datasetId = nmiCommand.Parameters.Add("$datasetId", SqliteType.Text);

        foreach (var summary in summaries)
        {
            datasetId.Value = summary.Id;
            using var reader = nmiCommand.ExecuteReader();
            while (reader.Read())
            {
                summary.Nmis.Add(reader.GetString(0));
            }
        }

        return summaries;
    }

    private ElectricityDataset? Get(string id)
    {
        using var connection = OpenConnection();
        using var datasetCommand = connection.CreateCommand();
        datasetCommand.CommandText =
            """
            SELECT SchemaVersion, Name, SourceFileName, ImportedAtUtcTicks
            FROM Datasets
            WHERE Id = $id;
            """;
        datasetCommand.Parameters.AddWithValue("$id", id);

        ElectricityDataset dataset;
        using (var reader = datasetCommand.ExecuteReader())
        {
            if (!reader.Read())
            {
                return null;
            }

            dataset = new ElectricityDataset
            {
                Id = id,
                SchemaVersion = reader.GetInt32(0),
                Name = reader.GetString(1),
                SourceFileName = reader.GetString(2),
                ImportedAt = new DateTimeOffset(reader.GetInt64(3), TimeSpan.Zero)
            };
        }

        using var channelCommand = connection.CreateCommand();
        channelCommand.CommandText =
            """
            SELECT
                Id,
                Nmi,
                NmiConfiguration,
                RegisterId,
                NmiSuffix,
                DataStreamIdentifier,
                MeterSerialNumber,
                Unit,
                IntervalMinutes,
                Direction
            FROM Channels
            WHERE DatasetId = $datasetId
            ORDER BY Id;
            """;
        channelCommand.Parameters.AddWithValue("$datasetId", id);

        using (var reader = channelCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                dataset.Channels.Add(new Nem12Channel
                {
                    Id = reader.GetString(0),
                    Nmi = reader.GetString(1),
                    NmiConfiguration = reader.GetString(2),
                    RegisterId = GetNullableString(reader, 3),
                    NmiSuffix = reader.GetString(4),
                    DataStreamIdentifier = GetNullableString(reader, 5),
                    MeterSerialNumber = GetNullableString(reader, 6),
                    Unit = reader.GetString(7),
                    IntervalMinutes = reader.GetInt32(8),
                    Direction = (EnergyFlowDirection)reader.GetInt32(9)
                });
            }
        }

        using var warningCommand = connection.CreateCommand();
        warningCommand.CommandText =
            """
            SELECT Text
            FROM Warnings
            WHERE DatasetId = $datasetId
            ORDER BY Ordinal;
            """;
        warningCommand.Parameters.AddWithValue("$datasetId", id);

        using (var reader = warningCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                dataset.Warnings.Add(reader.GetString(0));
            }
        }

        using var readingCommand = connection.CreateCommand();
        readingCommand.CommandText =
            """
            SELECT ChannelId, TimestampTicks, EnergyKwh
            FROM Readings
            WHERE DatasetId = $datasetId
            ORDER BY ChannelId, TimestampTicks;
            """;
        readingCommand.Parameters.AddWithValue("$datasetId", id);

        using (var reader = readingCommand.ExecuteReader())
        {
            while (reader.Read())
            {
                dataset.Readings.Add(new Nem12Reading
                {
                    ChannelId = reader.GetString(0),
                    Timestamp = new DateTime(reader.GetInt64(1), DateTimeKind.Unspecified),
                    EnergyKwh = reader.GetDouble(2)
                });
            }
        }

        return dataset;
    }

    private void Save(ElectricityDataset dataset)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        using (var deleteCommand = connection.CreateCommand())
        {
            deleteCommand.Transaction = transaction;
            deleteCommand.CommandText = "DELETE FROM Datasets WHERE Id = $id;";
            deleteCommand.Parameters.AddWithValue("$id", dataset.Id);
            deleteCommand.ExecuteNonQuery();
        }

        using (var datasetCommand = connection.CreateCommand())
        {
            datasetCommand.Transaction = transaction;
            datasetCommand.CommandText =
                """
                INSERT INTO Datasets (Id, SchemaVersion, Name, SourceFileName, ImportedAtUtcTicks)
                VALUES ($id, $schemaVersion, $name, $sourceFileName, $importedAtUtcTicks);
                """;
            datasetCommand.Parameters.AddWithValue("$id", dataset.Id);
            datasetCommand.Parameters.AddWithValue("$schemaVersion", dataset.SchemaVersion);
            datasetCommand.Parameters.AddWithValue("$name", dataset.Name);
            datasetCommand.Parameters.AddWithValue("$sourceFileName", dataset.SourceFileName);
            datasetCommand.Parameters.AddWithValue("$importedAtUtcTicks", dataset.ImportedAt.UtcTicks);
            datasetCommand.ExecuteNonQuery();
        }

        using (var channelCommand = connection.CreateCommand())
        {
            channelCommand.Transaction = transaction;
            channelCommand.CommandText =
                """
                INSERT INTO Channels (
                    DatasetId,
                    Id,
                    Nmi,
                    NmiConfiguration,
                    RegisterId,
                    NmiSuffix,
                    DataStreamIdentifier,
                    MeterSerialNumber,
                    Unit,
                    IntervalMinutes,
                    Direction)
                VALUES (
                    $datasetId,
                    $id,
                    $nmi,
                    $nmiConfiguration,
                    $registerId,
                    $nmiSuffix,
                    $dataStreamIdentifier,
                    $meterSerialNumber,
                    $unit,
                    $intervalMinutes,
                    $direction);
                """;

            var datasetId = channelCommand.Parameters.Add("$datasetId", SqliteType.Text);
            var id = channelCommand.Parameters.Add("$id", SqliteType.Text);
            var nmi = channelCommand.Parameters.Add("$nmi", SqliteType.Text);
            var nmiConfiguration = channelCommand.Parameters.Add("$nmiConfiguration", SqliteType.Text);
            var registerId = channelCommand.Parameters.Add("$registerId", SqliteType.Text);
            var nmiSuffix = channelCommand.Parameters.Add("$nmiSuffix", SqliteType.Text);
            var dataStreamIdentifier = channelCommand.Parameters.Add("$dataStreamIdentifier", SqliteType.Text);
            var meterSerialNumber = channelCommand.Parameters.Add("$meterSerialNumber", SqliteType.Text);
            var unit = channelCommand.Parameters.Add("$unit", SqliteType.Text);
            var intervalMinutes = channelCommand.Parameters.Add("$intervalMinutes", SqliteType.Integer);
            var direction = channelCommand.Parameters.Add("$direction", SqliteType.Integer);

            channelCommand.Prepare();
            foreach (var channel in dataset.Channels)
            {
                datasetId.Value = dataset.Id;
                id.Value = channel.Id;
                nmi.Value = channel.Nmi;
                nmiConfiguration.Value = channel.NmiConfiguration;
                registerId.Value = channel.RegisterId ?? (object)DBNull.Value;
                nmiSuffix.Value = channel.NmiSuffix;
                dataStreamIdentifier.Value = channel.DataStreamIdentifier ?? (object)DBNull.Value;
                meterSerialNumber.Value = channel.MeterSerialNumber ?? (object)DBNull.Value;
                unit.Value = channel.Unit;
                intervalMinutes.Value = channel.IntervalMinutes;
                direction.Value = (int)channel.Direction;
                channelCommand.ExecuteNonQuery();
            }
        }

        using (var warningCommand = connection.CreateCommand())
        {
            warningCommand.Transaction = transaction;
            warningCommand.CommandText =
                """
                INSERT INTO Warnings (DatasetId, Ordinal, Text)
                VALUES ($datasetId, $ordinal, $text);
                """;

            var datasetId = warningCommand.Parameters.Add("$datasetId", SqliteType.Text);
            var ordinal = warningCommand.Parameters.Add("$ordinal", SqliteType.Integer);
            var text = warningCommand.Parameters.Add("$text", SqliteType.Text);

            warningCommand.Prepare();
            for (var index = 0; index < dataset.Warnings.Count; index++)
            {
                datasetId.Value = dataset.Id;
                ordinal.Value = index;
                text.Value = dataset.Warnings[index];
                warningCommand.ExecuteNonQuery();
            }
        }

        using (var readingCommand = connection.CreateCommand())
        {
            readingCommand.Transaction = transaction;
            readingCommand.CommandText =
                """
                INSERT INTO Readings (DatasetId, ChannelId, TimestampTicks, EnergyKwh)
                VALUES ($datasetId, $channelId, $timestampTicks, $energyKwh);
                """;

            var datasetId = readingCommand.Parameters.Add("$datasetId", SqliteType.Text);
            var channelId = readingCommand.Parameters.Add("$channelId", SqliteType.Text);
            var timestampTicks = readingCommand.Parameters.Add("$timestampTicks", SqliteType.Integer);
            var energyKwh = readingCommand.Parameters.Add("$energyKwh", SqliteType.Real);

            datasetId.Value = dataset.Id;
            readingCommand.Prepare();

            foreach (var reading in dataset.Readings)
            {
                channelId.Value = reading.ChannelId;
                timestampTicks.Value = reading.Timestamp.Ticks;
                energyKwh.Value = reading.EnergyKwh;
                readingCommand.ExecuteNonQuery();
            }
        }

        transaction.Commit();
    }

    private void Delete(string id)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM Datasets WHERE Id = $id;";
        command.Parameters.AddWithValue("$id", id);
        command.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();

        using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                PRAGMA foreign_keys = ON;
                PRAGMA busy_timeout = 5000;
                """;
            command.ExecuteNonQuery();
        }

        EnsureDatabase(connection);
        return connection;
    }

    private void EnsureDatabase(SqliteConnection connection)
    {
        if (_initialized)
        {
            return;
        }

        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS Datasets (
                Id TEXT PRIMARY KEY,
                SchemaVersion INTEGER NOT NULL,
                Name TEXT NOT NULL,
                SourceFileName TEXT NOT NULL,
                ImportedAtUtcTicks INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Channels (
                DatasetId TEXT NOT NULL,
                Id TEXT NOT NULL,
                Nmi TEXT NOT NULL,
                NmiConfiguration TEXT NOT NULL,
                RegisterId TEXT NULL,
                NmiSuffix TEXT NOT NULL,
                DataStreamIdentifier TEXT NULL,
                MeterSerialNumber TEXT NULL,
                Unit TEXT NOT NULL,
                IntervalMinutes INTEGER NOT NULL,
                Direction INTEGER NOT NULL,
                PRIMARY KEY (DatasetId, Id),
                FOREIGN KEY (DatasetId) REFERENCES Datasets(Id) ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE TABLE IF NOT EXISTS Readings (
                DatasetId TEXT NOT NULL,
                ChannelId TEXT NOT NULL,
                TimestampTicks INTEGER NOT NULL,
                EnergyKwh REAL NOT NULL,
                PRIMARY KEY (DatasetId, ChannelId, TimestampTicks),
                FOREIGN KEY (DatasetId, ChannelId)
                    REFERENCES Channels(DatasetId, Id)
                    ON DELETE CASCADE
            ) WITHOUT ROWID;

            CREATE INDEX IF NOT EXISTS IX_Readings_Dataset_Timestamp
                ON Readings (DatasetId, TimestampTicks);

            CREATE TABLE IF NOT EXISTS Warnings (
                DatasetId TEXT NOT NULL,
                Ordinal INTEGER NOT NULL,
                Text TEXT NOT NULL,
                PRIMARY KEY (DatasetId, Ordinal),
                FOREIGN KEY (DatasetId) REFERENCES Datasets(Id) ON DELETE CASCADE
            ) WITHOUT ROWID;
            """;
        command.ExecuteNonQuery();
        _initialized = true;
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
}
