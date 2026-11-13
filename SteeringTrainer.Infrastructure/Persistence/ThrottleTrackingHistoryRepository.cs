using Microsoft.Data.Sqlite;
using SteeringTrainer.Core.Training.ThrottleTracking;
using System;
using System.Collections.Generic;
using System.IO;

namespace SteeringTrainer.Infrastructure.Persistence
{
    /// <summary>
    /// アクセル波形追従トレーニングの結果をSQLiteに永続化するリポジトリ
    /// </summary>
    public sealed class ThrottleTrackingHistoryRepository
    {
        private readonly string _connectionString;

        public ThrottleTrackingHistoryRepository(string? databasePath = null)
        {
            databasePath ??= Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SteeringTrainer",
                "training-history.db");
            Directory.CreateDirectory(Path.GetDirectoryName(databasePath)!);
            _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath }.ToString();
            Initialize();
        }

        public ThrottleTrackingHistoryEntry Save(ThrottleTrackingResult result)
        {
            var recordedAt = DateTime.UtcNow;
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO ThrottleTrackingAttempts
                    (RecordedAtUtc, TotalScore, WaveformScore, PhaseScore, BrakeStabilityScore,
                     PhaseLagSeconds, BrakeInToleranceRatio, ThrottleInToleranceRatio, BothInToleranceRatio,
                     RisingMeanError, FallingMeanError, RisingBrakeRatio, FallingBrakeRatio,
                     ElapsedSeconds, TotalSamples, TargetBrake, BrakeTolerance,
                     ThrottleMin, ThrottleMax, ThrottleTolerance, CycleDuration, Grade)
                VALUES
                    ($recorded, $totalScore, $waveformScore, $phaseScore, $brakeStabilityScore,
                     $phaseLag, $brakeRatio, $throttleRatio, $bothRatio,
                     $risingError, $fallingError, $risingBrake, $fallingBrake,
                     $elapsed, $samples, $targetBrake, $brakeTol,
                     $throttleMin, $throttleMax, $throttleTol, $cycleDur, $grade);
                SELECT last_insert_rowid();
                """;

            command.Parameters.AddWithValue("$recorded", recordedAt.ToString("O"));
            command.Parameters.AddWithValue("$totalScore", result.TotalScore);
            command.Parameters.AddWithValue("$waveformScore", result.WaveformScore);
            command.Parameters.AddWithValue("$phaseScore", result.PhaseScore);
            command.Parameters.AddWithValue("$brakeStabilityScore", result.BrakeStabilityScore);
            command.Parameters.AddWithValue("$phaseLag", result.PhaseLagSeconds);
            command.Parameters.AddWithValue("$brakeRatio", result.BrakeInToleranceRatio);
            command.Parameters.AddWithValue("$throttleRatio", result.ThrottleInToleranceRatio);
            command.Parameters.AddWithValue("$bothRatio", result.BothInToleranceRatio);
            command.Parameters.AddWithValue("$risingError", result.RisingMeanError);
            command.Parameters.AddWithValue("$fallingError", result.FallingMeanError);
            command.Parameters.AddWithValue("$risingBrake", result.RisingBrakeRatio);
            command.Parameters.AddWithValue("$fallingBrake", result.FallingBrakeRatio);
            command.Parameters.AddWithValue("$elapsed", result.ElapsedSeconds);
            command.Parameters.AddWithValue("$samples", result.TotalSamples);
            command.Parameters.AddWithValue("$targetBrake", result.ConfigTargetBrake);
            command.Parameters.AddWithValue("$brakeTol", result.ConfigBrakeTolerance);
            command.Parameters.AddWithValue("$throttleMin", result.ConfigThrottleMin);
            command.Parameters.AddWithValue("$throttleMax", result.ConfigThrottleMax);
            command.Parameters.AddWithValue("$throttleTol", result.ConfigThrottleTolerance);
            command.Parameters.AddWithValue("$cycleDur", result.ConfigCycleDuration);
            command.Parameters.AddWithValue("$grade", result.Grade);

            var id = (long)command.ExecuteScalar()!;
            result.Id = id;
            result.RecordedAtUtc = recordedAt;

            return new ThrottleTrackingHistoryEntry(
                id,
                recordedAt,
                result.TotalScore,
                result.WaveformScore,
                result.PhaseScore,
                result.BrakeStabilityScore,
                result.PhaseLagSeconds,
                result.BrakeInToleranceRatio,
                result.ThrottleInToleranceRatio,
                result.BothInToleranceRatio,
                result.RisingMeanError,
                result.FallingMeanError,
                result.RisingBrakeRatio,
                result.FallingBrakeRatio,
                result.ElapsedSeconds,
                result.TotalSamples,
                result.ConfigTargetBrake,
                result.ConfigBrakeTolerance,
                result.ConfigThrottleMin,
                result.ConfigThrottleMax,
                result.ConfigThrottleTolerance,
                result.ConfigCycleDuration,
                result.Grade);
        }

        public IReadOnlyList<ThrottleTrackingHistoryEntry> LoadAll()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT Id, RecordedAtUtc, TotalScore, WaveformScore, PhaseScore, BrakeStabilityScore,
                       PhaseLagSeconds, BrakeInToleranceRatio, ThrottleInToleranceRatio, BothInToleranceRatio,
                       RisingMeanError, FallingMeanError, RisingBrakeRatio, FallingBrakeRatio,
                       ElapsedSeconds, TotalSamples, TargetBrake, BrakeTolerance,
                       ThrottleMin, ThrottleMax, ThrottleTolerance, CycleDuration, Grade
                FROM ThrottleTrackingAttempts
                ORDER BY Id DESC;
                """;

            using var reader = command.ExecuteReader();
            var list = new List<ThrottleTrackingHistoryEntry>();
            while (reader.Read())
            {
                list.Add(new ThrottleTrackingHistoryEntry(
                    reader.GetInt64(0),
                    DateTime.Parse(reader.GetString(1), null, System.Globalization.DateTimeStyles.RoundtripKind),
                    reader.GetDouble(2),
                    reader.GetDouble(3),
                    reader.GetDouble(4),
                    reader.GetDouble(5),
                    reader.GetDouble(6),
                    reader.GetDouble(7),
                    reader.GetDouble(8),
                    reader.GetDouble(9),
                    reader.GetDouble(10),
                    reader.GetDouble(11),
                    reader.GetDouble(12),
                    reader.GetDouble(13),
                    reader.GetDouble(14),
                    reader.GetInt32(15),
                    reader.GetDouble(16),
                    reader.GetDouble(17),
                    reader.GetDouble(18),
                    reader.GetDouble(19),
                    reader.GetDouble(20),
                    reader.GetDouble(21),
                    reader.GetString(22)
                ));
            }
            return list;
        }

        private void Initialize()
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS ThrottleTrackingAttempts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    RecordedAtUtc TEXT NOT NULL,
                    TotalScore REAL NOT NULL,
                    WaveformScore REAL NOT NULL,
                    PhaseScore REAL NOT NULL,
                    BrakeStabilityScore REAL NOT NULL,
                    PhaseLagSeconds REAL NOT NULL,
                    BrakeInToleranceRatio REAL NOT NULL,
                    ThrottleInToleranceRatio REAL NOT NULL,
                    BothInToleranceRatio REAL NOT NULL,
                    RisingMeanError REAL NOT NULL,
                    FallingMeanError REAL NOT NULL,
                    RisingBrakeRatio REAL NOT NULL,
                    FallingBrakeRatio REAL NOT NULL,
                    ElapsedSeconds REAL NOT NULL,
                    TotalSamples INTEGER NOT NULL,
                    TargetBrake REAL NOT NULL,
                    BrakeTolerance REAL NOT NULL,
                    ThrottleMin REAL NOT NULL,
                    ThrottleMax REAL NOT NULL,
                    ThrottleTolerance REAL NOT NULL,
                    CycleDuration REAL NOT NULL,
                    Grade TEXT NOT NULL
                );
                """;
            command.ExecuteNonQuery();
        }
    }
}
