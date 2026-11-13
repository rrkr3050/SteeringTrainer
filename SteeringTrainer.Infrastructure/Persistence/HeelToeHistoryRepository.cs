using Microsoft.Data.Sqlite;
using SteeringTrainer.Core.Training.HeelToe;

namespace SteeringTrainer.Infrastructure.Persistence;

/// <summary>Persists compact Heel &amp; Toe scores for the target-pair results map.</summary>
public sealed class HeelToeHistoryRepository
{
    private readonly string _connectionString;

    public HeelToeHistoryRepository(string? databasePath = null)
    {
        databasePath ??= Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SteeringTrainer",
            "training-history.db");
        Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
        Initialize();
    }

    public HeelToeHistoryEntry Save(HeelToeAttemptResult result, bool isChallenge)
    {
        var recordedAt = DateTime.UtcNow;
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO HeelToeAttempts
                (RecordedAtUtc, TargetBrake, TargetThrottle, Score, BrakeStabilityScore, TimingScore, AmountScore, Grade, IsChallenge)
            VALUES
                ($recorded, $brake, $throttle, $score, $stability, $timing, $amount, $grade, $challenge);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$recorded", recordedAt.ToString("O"));
        command.Parameters.AddWithValue("$brake", result.TargetBrake);
        command.Parameters.AddWithValue("$throttle", result.TargetThrottle);
        command.Parameters.AddWithValue("$score", result.TotalScore);
        command.Parameters.AddWithValue("$stability", result.BrakeStabilityScore);
        command.Parameters.AddWithValue("$timing", result.BlipTimingScore);
        command.Parameters.AddWithValue("$amount", result.BlipAmountScore);
        command.Parameters.AddWithValue("$grade", result.Grade);
        command.Parameters.AddWithValue("$challenge", isChallenge ? 1 : 0);
        var id = (long)command.ExecuteScalar()!;
        return new HeelToeHistoryEntry(id, recordedAt, result.TargetBrake, result.TargetThrottle,
            result.TotalScore, result.BrakeStabilityScore, result.BlipTimingScore, result.BlipAmountScore, result.Grade, isChallenge);
    }

    public IReadOnlyList<HeelToeHistoryEntry> LoadAll()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, RecordedAtUtc, TargetBrake, TargetThrottle, Score,
                   BrakeStabilityScore, TimingScore, AmountScore, Grade, IsChallenge
            FROM HeelToeAttempts
            ORDER BY Id;
            """;
        using var reader = command.ExecuteReader();
        var records = new List<HeelToeHistoryEntry>();
        while (reader.Read())
        {
            records.Add(new HeelToeHistoryEntry(
                reader.GetInt64(0), DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4), reader.GetDouble(5),
                reader.GetDouble(6), reader.GetDouble(7), reader.GetString(8), reader.GetInt32(9) != 0));
        }
        return records;
    }

    public BrakeHoldHistoryEntry SaveBrakeHold(double targetBrake, double elapsedSeconds)
    {
        var recordedAt = DateTime.UtcNow;
        var score = Math.Clamp(100.0 - elapsedSeconds * 10.0, 0.0, 100.0);
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO BrakeHoldAttempts (RecordedAtUtc, TargetBrake, ElapsedSeconds, Score)
            VALUES ($recorded, $brake, $elapsed, $score);
            SELECT last_insert_rowid();
            """;
        command.Parameters.AddWithValue("$recorded", recordedAt.ToString("O"));
        command.Parameters.AddWithValue("$brake", targetBrake);
        command.Parameters.AddWithValue("$elapsed", elapsedSeconds);
        command.Parameters.AddWithValue("$score", score);
        var id = (long)command.ExecuteScalar()!;
        return new BrakeHoldHistoryEntry(id, recordedAt, targetBrake, elapsedSeconds, score);
    }

    public IReadOnlyList<BrakeHoldHistoryEntry> LoadBrakeHoldAll()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, RecordedAtUtc, TargetBrake, ElapsedSeconds, Score
            FROM BrakeHoldAttempts ORDER BY Id;
            """;
        using var reader = command.ExecuteReader();
        var records = new List<BrakeHoldHistoryEntry>();
        while (reader.Read())
        {
            records.Add(new BrakeHoldHistoryEntry(reader.GetInt64(0),
                DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                reader.GetDouble(2), reader.GetDouble(3), reader.GetDouble(4)));
        }
        return records;
    }

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS HeelToeAttempts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecordedAtUtc TEXT NOT NULL,
                TargetBrake REAL NOT NULL,
                TargetThrottle REAL NOT NULL,
                Score REAL NOT NULL,
                BrakeStabilityScore REAL NOT NULL,
                TimingScore REAL NOT NULL,
                AmountScore REAL NOT NULL,
                Grade TEXT NOT NULL,
                IsChallenge INTEGER NOT NULL DEFAULT 1
            );
            CREATE TABLE IF NOT EXISTS BrakeHoldAttempts (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                RecordedAtUtc TEXT NOT NULL,
                TargetBrake REAL NOT NULL,
                ElapsedSeconds REAL NOT NULL,
                Score REAL NOT NULL
            );
            """;
        command.ExecuteNonQuery();
        using var columns = connection.CreateCommand();
        columns.CommandText = "PRAGMA table_info(HeelToeAttempts);";
        using var reader = columns.ExecuteReader();
        var hasModeColumn = false;
        while (reader.Read())
            if (reader.GetString(1).Equals("IsChallenge", StringComparison.OrdinalIgnoreCase))
                hasModeColumn = true;
        reader.Close();
        if (!hasModeColumn)
        {
            using var migrate = connection.CreateCommand();
            migrate.CommandText = "ALTER TABLE HeelToeAttempts ADD COLUMN IsChallenge INTEGER NOT NULL DEFAULT 1;";
            migrate.ExecuteNonQuery();
        }
    }
}
