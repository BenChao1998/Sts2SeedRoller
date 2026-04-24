using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using SeedModel.Neow;
using SeedModel.Sts2;

namespace SeedUi.Storage;

internal sealed class SeedArchiveDatabase
{
    public const int CurrentSchemaVersion = 1;

    private readonly string _connectionString;

    public SeedArchiveDatabase(string databasePath, SeedArchiveVersionInfo versionInfo)
    {
        DatabasePath = databasePath ?? throw new ArgumentNullException(nameof(databasePath));
        VersionInfo = versionInfo ?? throw new ArgumentNullException(nameof(versionInfo));
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public string DatabasePath { get; }

    public SeedArchiveVersionInfo VersionInfo { get; }

    public static string BuildDatabasePath(string baseDirectory, SeedArchiveVersionInfo versionInfo)
    {
        var fingerprint = ComputeFingerprint(versionInfo);
        var directory = Path.Combine(baseDirectory, "data", "seed_cache");
        Directory.CreateDirectory(directory);
        return Path.Combine(directory, $"seed_archive_{fingerprint}.db");
    }

    public void EnsureCreated()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DatabasePath) ?? AppContext.BaseDirectory);
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS db_meta (
                meta_key TEXT PRIMARY KEY,
                meta_value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS seed_jobs (
                job_id TEXT PRIMARY KEY,
                mode TEXT NOT NULL,
                status TEXT NOT NULL,
                character TEXT NOT NULL,
                ascension INTEGER NOT NULL,
                start_seed_text TEXT NOT NULL,
                seed_step INTEGER NOT NULL,
                sequence_token TEXT NOT NULL,
                next_index INTEGER NOT NULL,
                requested_count INTEGER NOT NULL,
                processed_count INTEGER NOT NULL,
                stored_count INTEGER NOT NULL,
                skipped_count INTEGER NOT NULL,
                last_seed_text TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS seed_runs (
                run_id INTEGER PRIMARY KEY AUTOINCREMENT,
                job_id TEXT NOT NULL,
                seed_text TEXT NOT NULL,
                seed_value INTEGER NOT NULL,
                seed_order_value INTEGER NOT NULL,
                character TEXT NOT NULL,
                ascension INTEGER NOT NULL,
                act2_ancient_id TEXT NULL,
                act3_ancient_id TEXT NULL,
                created_at TEXT NOT NULL,
                UNIQUE(seed_text, character, ascension),
                FOREIGN KEY (job_id) REFERENCES seed_jobs(job_id)
            );

            CREATE TABLE IF NOT EXISTS act1_options (
                run_id INTEGER NOT NULL,
                option_index INTEGER NOT NULL,
                option_id TEXT NOT NULL,
                relic_id TEXT NOT NULL,
                pool TEXT NOT NULL,
                kind TEXT NOT NULL,
                title TEXT NULL,
                description TEXT NULL,
                note TEXT NULL,
                PRIMARY KEY (run_id, option_index),
                FOREIGN KEY (run_id) REFERENCES seed_runs(run_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS act1_reward_details (
                run_id INTEGER NOT NULL,
                option_index INTEGER NOT NULL,
                detail_index INTEGER NOT NULL,
                detail_type TEXT NOT NULL,
                label TEXT NOT NULL,
                value_text TEXT NOT NULL,
                model_id TEXT NULL,
                amount INTEGER NULL,
                PRIMARY KEY (run_id, option_index, detail_index),
                FOREIGN KEY (run_id, option_index) REFERENCES act1_options(run_id, option_index) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS act_ancients (
                run_id INTEGER NOT NULL,
                act_number INTEGER NOT NULL,
                ancient_id TEXT NULL,
                ancient_name TEXT NULL,
                PRIMARY KEY (run_id, act_number),
                FOREIGN KEY (run_id) REFERENCES seed_runs(run_id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS act_ancient_options (
                run_id INTEGER NOT NULL,
                act_number INTEGER NOT NULL,
                option_index INTEGER NOT NULL,
                option_id TEXT NOT NULL,
                title TEXT NULL,
                description TEXT NULL,
                relic_id TEXT NULL,
                note TEXT NULL,
                was_chosen INTEGER NOT NULL,
                PRIMARY KEY (run_id, act_number, option_index),
                FOREIGN KEY (run_id, act_number) REFERENCES act_ancients(run_id, act_number) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_seed_runs_character_ascension_seed
                ON seed_runs(character, ascension, seed_order_value);
            CREATE INDEX IF NOT EXISTS idx_seed_runs_act2
                ON seed_runs(act2_ancient_id);
            CREATE INDEX IF NOT EXISTS idx_seed_runs_act3
                ON seed_runs(act3_ancient_id);
            CREATE INDEX IF NOT EXISTS idx_act1_options_relic
                ON act1_options(relic_id);
            CREATE INDEX IF NOT EXISTS idx_act1_reward_details_lookup
                ON act1_reward_details(detail_type, model_id);
            CREATE INDEX IF NOT EXISTS idx_act_ancients_lookup
                ON act_ancients(act_number, ancient_id);
            CREATE INDEX IF NOT EXISTS idx_act_ancient_options_lookup
                ON act_ancient_options(act_number, option_id);
            """;
        command.ExecuteNonQuery();

        UpsertMeta(connection, "schema_version", VersionInfo.SchemaVersion.ToString(CultureInfo.InvariantCulture));
        UpsertMeta(connection, "data_version", VersionInfo.DataVersion);
        UpsertMeta(connection, "app_version", VersionInfo.AppVersion);
    }

    public SeedArchiveDatabaseSummary GetSummary()
    {
        using var connection = OpenConnection();
        var jobCount = ExecuteScalarInt(connection, "SELECT COUNT(*) FROM seed_jobs;");
        var runCount = ExecuteScalarInt(connection, "SELECT COUNT(*) FROM seed_runs;");
        return new SeedArchiveDatabaseSummary
        {
            DatabasePath = DatabasePath,
            VersionInfo = VersionInfo,
            JobCount = jobCount,
            RunCount = runCount,
            LatestJob = GetRecentJobs(1).FirstOrDefault()
        };
    }

    public SeedArchiveScanJob CreateJob(SeedArchiveJobCreateRequest request)
    {
        var job = new SeedArchiveScanJob
        {
            JobId = Guid.NewGuid().ToString("N"),
            Mode = request.Mode,
            Status = SeedArchiveJobStatus.Pending,
            Character = request.Character,
            Ascension = request.Ascension,
            StartSeedText = request.StartSeedText,
            SeedStep = request.SeedStep,
            SequenceToken = request.SequenceToken,
            NextIndex = 0,
            RequestedCount = request.RequestedCount,
            ProcessedCount = 0,
            StoredCount = 0,
            SkippedCount = 0,
            LastSeedText = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO seed_jobs (
                job_id, mode, status, character, ascension, start_seed_text, seed_step, sequence_token,
                next_index, requested_count, processed_count, stored_count, skipped_count, last_seed_text,
                created_at, updated_at)
            VALUES (
                $job_id, $mode, $status, $character, $ascension, $start_seed_text, $seed_step, $sequence_token,
                $next_index, $requested_count, $processed_count, $stored_count, $skipped_count, $last_seed_text,
                $created_at, $updated_at);
            """;
        BindJobParameters(command, job);
        command.ExecuteNonQuery();
        return job;
    }

    public SeedArchiveScanJob UpdateJobStatus(string jobId, SeedArchiveJobStatus status)
    {
        var existing = GetJob(jobId) ?? throw new InvalidOperationException($"未找到铺种任务：{jobId}");
        var updated = existing with
        {
            Status = status,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE seed_jobs
            SET status = $status,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$job_id", updated.JobId);
        command.Parameters.AddWithValue("$status", updated.Status.ToString());
        command.Parameters.AddWithValue("$updated_at", updated.UpdatedAt.ToString("O"));
        command.ExecuteNonQuery();
        return updated;
    }

    public SeedArchiveBatchWriteResult SaveBatch(
        string jobId,
        IReadOnlyList<SeedArchiveStoredRun> runs,
        int nextIndex,
        string? lastSeedText,
        bool markCompleted)
    {
        using var connection = OpenConnection();
        using var transaction = connection.BeginTransaction();

        var existingJob = GetJob(connection, jobId) ?? throw new InvalidOperationException($"未找到铺种任务：{jobId}");
        var insertedRuns = 0;
        var skippedRuns = 0;

        foreach (var run in runs)
        {
            var runId = InsertRun(connection, transaction, run);
            if (runId == null)
            {
                skippedRuns++;
                continue;
            }

            insertedRuns++;
            InsertAct1Options(connection, transaction, runId.Value, run.Act1Options);
            InsertAncientPreview(connection, transaction, runId.Value, run.Sts2Preview);
        }

        var updatedJob = existingJob with
        {
            Status = markCompleted ? SeedArchiveJobStatus.Completed : SeedArchiveJobStatus.Paused,
            NextIndex = nextIndex,
            RequestedCount = existingJob.RequestedCount + runs.Count,
            ProcessedCount = existingJob.ProcessedCount + runs.Count,
            StoredCount = existingJob.StoredCount + insertedRuns,
            SkippedCount = existingJob.SkippedCount + skippedRuns,
            LastSeedText = lastSeedText ?? existingJob.LastSeedText,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        using var updateCommand = connection.CreateCommand();
        updateCommand.Transaction = transaction;
        updateCommand.CommandText =
            """
            UPDATE seed_jobs
            SET status = $status,
                next_index = $next_index,
                requested_count = $requested_count,
                processed_count = $processed_count,
                stored_count = $stored_count,
                skipped_count = $skipped_count,
                last_seed_text = $last_seed_text,
                updated_at = $updated_at
            WHERE job_id = $job_id;
            """;
        updateCommand.Parameters.AddWithValue("$job_id", updatedJob.JobId);
        updateCommand.Parameters.AddWithValue("$status", updatedJob.Status.ToString());
        updateCommand.Parameters.AddWithValue("$next_index", updatedJob.NextIndex);
        updateCommand.Parameters.AddWithValue("$requested_count", updatedJob.RequestedCount);
        updateCommand.Parameters.AddWithValue("$processed_count", updatedJob.ProcessedCount);
        updateCommand.Parameters.AddWithValue("$stored_count", updatedJob.StoredCount);
        updateCommand.Parameters.AddWithValue("$skipped_count", updatedJob.SkippedCount);
        updateCommand.Parameters.AddWithValue("$last_seed_text", (object?)updatedJob.LastSeedText ?? DBNull.Value);
        updateCommand.Parameters.AddWithValue("$updated_at", updatedJob.UpdatedAt.ToString("O"));
        updateCommand.ExecuteNonQuery();

        transaction.Commit();
        return new SeedArchiveBatchWriteResult(insertedRuns, skippedRuns, nextIndex, lastSeedText, updatedJob);
    }

    public SeedArchiveScanJob? GetJob(string jobId)
    {
        using var connection = OpenConnection();
        return GetJob(connection, jobId);
    }

    public IReadOnlyList<SeedArchiveScanJob> GetRecentJobs(int take)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT job_id, mode, status, character, ascension, start_seed_text, seed_step, sequence_token,
                   next_index, requested_count, processed_count, stored_count, skipped_count, last_seed_text,
                   created_at, updated_at
            FROM seed_jobs
            ORDER BY created_at DESC
            LIMIT $take;
            """;
        command.Parameters.AddWithValue("$take", take);
        using var reader = command.ExecuteReader();
        var jobs = new List<SeedArchiveScanJob>();
        while (reader.Read())
        {
            jobs.Add(ReadJob(reader));
        }

        return jobs;
    }

    public IReadOnlyList<SeedArchiveRunSummary> SearchRuns(SeedArchiveSearchCriteria criteria, int limit)
    {
        using var connection = OpenConnection();
        using var command = connection.CreateCommand();
        var sql = new StringBuilder(
            """
            SELECT DISTINCT r.run_id, r.job_id, r.seed_text, r.character, r.ascension, r.act2_ancient_id, r.act3_ancient_id, r.created_at
            FROM seed_runs r
            """);

        var conditions = new List<string>();
        AddMultiValueExistsConditions(
            conditions,
            command,
            criteria.Act1RelicIds,
            "$act1_relic_id_",
            valueParameter => $"EXISTS (SELECT 1 FROM act1_options o WHERE o.run_id = r.run_id AND o.relic_id = {valueParameter})");

        AddMultiValueExistsConditions(
            conditions,
            command,
            criteria.Act1CardIds,
            "$act1_card_id_",
            valueParameter => $"EXISTS (SELECT 1 FROM act1_reward_details d WHERE d.run_id = r.run_id AND d.detail_type = 'Card' AND d.model_id = {valueParameter})");

        AddMultiValueExistsConditions(
            conditions,
            command,
            criteria.Act1PotionIds,
            "$act1_potion_id_",
            valueParameter => $"EXISTS (SELECT 1 FROM act1_reward_details d WHERE d.run_id = r.run_id AND d.detail_type = 'Potion' AND d.model_id = {valueParameter})");

        if (!string.IsNullOrWhiteSpace(criteria.Act2AncientId))
        {
            conditions.Add("r.act2_ancient_id = $act2_ancient_id");
            command.Parameters.AddWithValue("$act2_ancient_id", criteria.Act2AncientId);
        }

        AddMultiValueExistsConditions(
            conditions,
            command,
            criteria.Act2OptionIds,
            "$act2_option_id_",
            valueParameter => $"EXISTS (SELECT 1 FROM act_ancient_options ao WHERE ao.run_id = r.run_id AND ao.act_number = 2 AND ao.option_id = {valueParameter})");

        if (!string.IsNullOrWhiteSpace(criteria.Act3AncientId))
        {
            conditions.Add("r.act3_ancient_id = $act3_ancient_id");
            command.Parameters.AddWithValue("$act3_ancient_id", criteria.Act3AncientId);
        }

        AddMultiValueExistsConditions(
            conditions,
            command,
            criteria.Act3OptionIds,
            "$act3_option_id_",
            valueParameter => $"EXISTS (SELECT 1 FROM act_ancient_options ao WHERE ao.run_id = r.run_id AND ao.act_number = 3 AND ao.option_id = {valueParameter})");

        if (!string.IsNullOrWhiteSpace(criteria.Character))
        {
            conditions.Add("r.character = $character");
            command.Parameters.AddWithValue("$character", criteria.Character);
        }

        if (criteria.Ascension.HasValue)
        {
            conditions.Add("r.ascension = $ascension");
            command.Parameters.AddWithValue("$ascension", criteria.Ascension.Value);
        }

        if (!string.IsNullOrWhiteSpace(criteria.SeedTextFrom))
        {
            conditions.Add("r.seed_order_value >= $seed_from");
            command.Parameters.AddWithValue("$seed_from", SeedOrderHelper.ToOrderValue(criteria.SeedTextFrom));
        }

        if (!string.IsNullOrWhiteSpace(criteria.SeedTextTo))
        {
            conditions.Add("r.seed_order_value <= $seed_to");
            command.Parameters.AddWithValue("$seed_to", SeedOrderHelper.ToOrderValue(criteria.SeedTextTo));
        }

        if (conditions.Count > 0)
        {
            sql.AppendLine();
            sql.Append("WHERE ");
            sql.Append(string.Join(" AND ", conditions));
        }

        sql.AppendLine();
        sql.Append(
            """
            ORDER BY r.created_at DESC
            LIMIT $limit;
            """);
        command.CommandText = sql.ToString();
        command.Parameters.AddWithValue("$limit", limit);

        using var reader = command.ExecuteReader();
        var rows = new List<SeedArchiveRunSummary>();
        while (reader.Read())
        {
            rows.Add(new SeedArchiveRunSummary
            {
                RunId = reader.GetInt64(0),
                JobId = reader.GetString(1),
                SeedText = reader.GetString(2),
                Character = reader.GetString(3),
                Ascension = reader.GetInt32(4),
                Act2AncientId = reader.IsDBNull(5) ? null : reader.GetString(5),
                Act3AncientId = reader.IsDBNull(6) ? null : reader.GetString(6),
                CreatedAt = DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture)
            });
        }

        return rows;
    }

    public SeedArchiveStoredRun? LoadRun(long runId)
    {
        using var connection = OpenConnection();
        using var runCommand = connection.CreateCommand();
        runCommand.CommandText =
            """
            SELECT run_id, job_id, seed_text, seed_value, seed_order_value, character, ascension
            FROM seed_runs
            WHERE run_id = $run_id;
            """;
        runCommand.Parameters.AddWithValue("$run_id", runId);
        using var runReader = runCommand.ExecuteReader();
        if (!runReader.Read())
        {
            return null;
        }

        var jobId = runReader.GetString(1);
        var seedText = runReader.GetString(2);
        var seedValue = unchecked((uint)runReader.GetInt64(3));
        var seedOrderValue = runReader.GetInt64(4);
        var character = runReader.GetString(5);
        var ascension = runReader.GetInt32(6);

        var act1Options = LoadAct1Options(connection, runId);
        var preview = new Sts2RunPreview
        {
            Seed = seedValue,
            SeedText = seedText
        };

        foreach (var act in LoadAncientActs(connection, runId))
        {
            preview.Acts.Add(act);
        }

        return new SeedArchiveStoredRun
        {
            RunId = runId,
            JobId = jobId,
            SeedText = seedText,
            SeedValue = seedValue,
            SeedOrderValue = seedOrderValue,
            Character = character,
            Ascension = ascension,
            Act1Options = act1Options,
            Sts2Preview = preview
        };
    }

    private SqliteConnection OpenConnection()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            PRAGMA foreign_keys = ON;
            PRAGMA temp_store = MEMORY;
            PRAGMA cache_size = -20000;
            """;
        command.ExecuteNonQuery();
        return connection;
    }

    private static string ComputeFingerprint(SeedArchiveVersionInfo versionInfo)
    {
        var raw = $"{versionInfo.SchemaVersion}|{versionInfo.DataVersion}|{versionInfo.AppVersion}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes[..8]).ToLowerInvariant();
    }

    private static void UpsertMeta(SqliteConnection connection, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO db_meta (meta_key, meta_value)
            VALUES ($key, $value)
            ON CONFLICT(meta_key) DO UPDATE SET meta_value = excluded.meta_value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static int ExecuteScalarInt(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    private static void AddMultiValueExistsConditions(
        ICollection<string> conditions,
        SqliteCommand command,
        IReadOnlyList<string>? values,
        string parameterPrefix,
        Func<string, string> conditionFactory)
    {
        if (values == null)
        {
            return;
        }

        var normalizedValues = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        for (var i = 0; i < normalizedValues.Count; i++)
        {
            var parameterName = $"{parameterPrefix}{i}";
            conditions.Add(conditionFactory(parameterName));
            command.Parameters.AddWithValue(parameterName, normalizedValues[i]);
        }
    }

    private static void BindJobParameters(SqliteCommand command, SeedArchiveScanJob job)
    {
        command.Parameters.AddWithValue("$job_id", job.JobId);
        command.Parameters.AddWithValue("$mode", job.Mode.ToString());
        command.Parameters.AddWithValue("$status", job.Status.ToString());
        command.Parameters.AddWithValue("$character", job.Character);
        command.Parameters.AddWithValue("$ascension", job.Ascension);
        command.Parameters.AddWithValue("$start_seed_text", job.StartSeedText);
        command.Parameters.AddWithValue("$seed_step", job.SeedStep);
        command.Parameters.AddWithValue("$sequence_token", job.SequenceToken);
        command.Parameters.AddWithValue("$next_index", job.NextIndex);
        command.Parameters.AddWithValue("$requested_count", job.RequestedCount);
        command.Parameters.AddWithValue("$processed_count", job.ProcessedCount);
        command.Parameters.AddWithValue("$stored_count", job.StoredCount);
        command.Parameters.AddWithValue("$skipped_count", job.SkippedCount);
        command.Parameters.AddWithValue("$last_seed_text", (object?)job.LastSeedText ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", job.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updated_at", job.UpdatedAt.ToString("O"));
    }

    private static SeedArchiveScanJob? GetJob(SqliteConnection connection, string jobId)
    {
        using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT job_id, mode, status, character, ascension, start_seed_text, seed_step, sequence_token,
                   next_index, requested_count, processed_count, stored_count, skipped_count, last_seed_text,
                   created_at, updated_at
            FROM seed_jobs
            WHERE job_id = $job_id;
            """;
        command.Parameters.AddWithValue("$job_id", jobId);
        using var reader = command.ExecuteReader();
        return reader.Read() ? ReadJob(reader) : null;
    }

    private static SeedArchiveScanJob ReadJob(SqliteDataReader reader)
    {
        return new SeedArchiveScanJob
        {
            JobId = reader.GetString(0),
            Mode = Enum.Parse<SeedArchiveMode>(reader.GetString(1)),
            Status = Enum.Parse<SeedArchiveJobStatus>(reader.GetString(2)),
            Character = reader.GetString(3),
            Ascension = reader.GetInt32(4),
            StartSeedText = reader.GetString(5),
            SeedStep = reader.GetInt32(6),
            SequenceToken = reader.GetString(7),
            NextIndex = reader.GetInt32(8),
            RequestedCount = reader.GetInt32(9),
            ProcessedCount = reader.GetInt32(10),
            StoredCount = reader.GetInt32(11),
            SkippedCount = reader.GetInt32(12),
            LastSeedText = reader.IsDBNull(13) ? null : reader.GetString(13),
            CreatedAt = DateTimeOffset.Parse(reader.GetString(14), CultureInfo.InvariantCulture),
            UpdatedAt = DateTimeOffset.Parse(reader.GetString(15), CultureInfo.InvariantCulture)
        };
    }

    private static long? InsertRun(SqliteConnection connection, SqliteTransaction transaction, SeedArchiveStoredRun run)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT OR IGNORE INTO seed_runs (
                job_id, seed_text, seed_value, seed_order_value, character, ascension,
                act2_ancient_id, act3_ancient_id, created_at)
            VALUES (
                $job_id, $seed_text, $seed_value, $seed_order_value, $character, $ascension,
                $act2_ancient_id, $act3_ancient_id, $created_at);
            """;
        command.Parameters.AddWithValue("$job_id", run.JobId);
        command.Parameters.AddWithValue("$seed_text", run.SeedText);
        command.Parameters.AddWithValue("$seed_value", unchecked((long)run.SeedValue));
        command.Parameters.AddWithValue("$seed_order_value", run.SeedOrderValue);
        command.Parameters.AddWithValue("$character", run.Character);
        command.Parameters.AddWithValue("$ascension", run.Ascension);
        command.Parameters.AddWithValue("$act2_ancient_id", (object?)run.Sts2Preview.Acts.FirstOrDefault(a => a.ActNumber == 2)?.AncientId ?? DBNull.Value);
        command.Parameters.AddWithValue("$act3_ancient_id", (object?)run.Sts2Preview.Acts.FirstOrDefault(a => a.ActNumber == 3)?.AncientId ?? DBNull.Value);
        command.Parameters.AddWithValue("$created_at", DateTimeOffset.UtcNow.ToString("O"));
        var affected = command.ExecuteNonQuery();
        if (affected <= 0)
        {
            return null;
        }

        using var idCommand = connection.CreateCommand();
        idCommand.Transaction = transaction;
        idCommand.CommandText = "SELECT last_insert_rowid();";
        return (long)(idCommand.ExecuteScalar() ?? 0L);
    }

    private static void InsertAct1Options(SqliteConnection connection, SqliteTransaction transaction, long runId, IReadOnlyList<NeowOptionResult> options)
    {
        for (var i = 0; i < options.Count; i++)
        {
            var option = options[i];
            using var optionCommand = connection.CreateCommand();
            optionCommand.Transaction = transaction;
            optionCommand.CommandText =
                """
                INSERT INTO act1_options (
                    run_id, option_index, option_id, relic_id, pool, kind, title, description, note)
                VALUES (
                    $run_id, $option_index, $option_id, $relic_id, $pool, $kind, $title, $description, $note);
                """;
            optionCommand.Parameters.AddWithValue("$run_id", runId);
            optionCommand.Parameters.AddWithValue("$option_index", i);
            optionCommand.Parameters.AddWithValue("$option_id", option.Id);
            optionCommand.Parameters.AddWithValue("$relic_id", option.RelicId);
            optionCommand.Parameters.AddWithValue("$pool", option.Pool);
            optionCommand.Parameters.AddWithValue("$kind", option.Kind.ToString());
            optionCommand.Parameters.AddWithValue("$title", (object?)option.Title ?? DBNull.Value);
            optionCommand.Parameters.AddWithValue("$description", (object?)option.Description ?? DBNull.Value);
            optionCommand.Parameters.AddWithValue("$note", (object?)option.Note ?? DBNull.Value);
            optionCommand.ExecuteNonQuery();

            for (var detailIndex = 0; detailIndex < option.Details.Count; detailIndex++)
            {
                var detail = option.Details[detailIndex];
                using var detailCommand = connection.CreateCommand();
                detailCommand.Transaction = transaction;
                detailCommand.CommandText =
                    """
                    INSERT INTO act1_reward_details (
                        run_id, option_index, detail_index, detail_type, label, value_text, model_id, amount)
                    VALUES (
                        $run_id, $option_index, $detail_index, $detail_type, $label, $value_text, $model_id, $amount);
                    """;
                detailCommand.Parameters.AddWithValue("$run_id", runId);
                detailCommand.Parameters.AddWithValue("$option_index", i);
                detailCommand.Parameters.AddWithValue("$detail_index", detailIndex);
                detailCommand.Parameters.AddWithValue("$detail_type", detail.Type.ToString());
                detailCommand.Parameters.AddWithValue("$label", detail.Label);
                detailCommand.Parameters.AddWithValue("$value_text", detail.Value);
                detailCommand.Parameters.AddWithValue("$model_id", (object?)detail.ModelId ?? DBNull.Value);
                detailCommand.Parameters.AddWithValue("$amount", (object?)detail.Amount ?? DBNull.Value);
                detailCommand.ExecuteNonQuery();
            }
        }
    }

    private static void InsertAncientPreview(SqliteConnection connection, SqliteTransaction transaction, long runId, Sts2RunPreview preview)
    {
        foreach (var act in preview.Acts)
        {
            using var actCommand = connection.CreateCommand();
            actCommand.Transaction = transaction;
            actCommand.CommandText =
                """
                INSERT INTO act_ancients (
                    run_id, act_number, ancient_id, ancient_name)
                VALUES (
                    $run_id, $act_number, $ancient_id, $ancient_name);
                """;
            actCommand.Parameters.AddWithValue("$run_id", runId);
            actCommand.Parameters.AddWithValue("$act_number", act.ActNumber);
            actCommand.Parameters.AddWithValue("$ancient_id", (object?)act.AncientId ?? DBNull.Value);
            actCommand.Parameters.AddWithValue("$ancient_name", (object?)act.AncientName ?? DBNull.Value);
            actCommand.ExecuteNonQuery();

            for (var optionIndex = 0; optionIndex < act.AncientOptions.Count; optionIndex++)
            {
                var option = act.AncientOptions[optionIndex];
                using var optionCommand = connection.CreateCommand();
                optionCommand.Transaction = transaction;
                optionCommand.CommandText =
                    """
                    INSERT INTO act_ancient_options (
                        run_id, act_number, option_index, option_id, title, description, relic_id, note, was_chosen)
                    VALUES (
                        $run_id, $act_number, $option_index, $option_id, $title, $description, $relic_id, $note, $was_chosen);
                    """;
                optionCommand.Parameters.AddWithValue("$run_id", runId);
                optionCommand.Parameters.AddWithValue("$act_number", act.ActNumber);
                optionCommand.Parameters.AddWithValue("$option_index", optionIndex);
                optionCommand.Parameters.AddWithValue("$option_id", option.OptionId);
                optionCommand.Parameters.AddWithValue("$title", (object?)option.Title ?? DBNull.Value);
                optionCommand.Parameters.AddWithValue("$description", (object?)option.Description ?? DBNull.Value);
                optionCommand.Parameters.AddWithValue("$relic_id", (object?)option.RelicId ?? DBNull.Value);
                optionCommand.Parameters.AddWithValue("$note", (object?)option.Note ?? DBNull.Value);
                optionCommand.Parameters.AddWithValue("$was_chosen", option.WasChosen ? 1 : 0);
                optionCommand.ExecuteNonQuery();
            }
        }
    }

    private static IReadOnlyList<NeowOptionResult> LoadAct1Options(SqliteConnection connection, long runId)
    {
        using var optionCommand = connection.CreateCommand();
        optionCommand.CommandText =
            """
            SELECT option_index, option_id, relic_id, pool, kind, title, description, note
            FROM act1_options
            WHERE run_id = $run_id
            ORDER BY option_index;
            """;
        optionCommand.Parameters.AddWithValue("$run_id", runId);
        using var optionReader = optionCommand.ExecuteReader();
        var rows = new List<(int Index, NeowOptionResult Option)>();
        while (optionReader.Read())
        {
            var optionIndex = optionReader.GetInt32(0);
            var details = LoadAct1Details(connection, runId, optionIndex);
            rows.Add((optionIndex, new NeowOptionResult
            {
                Id = optionReader.GetString(1),
                RelicId = optionReader.GetString(2),
                Pool = optionReader.GetString(3),
                Kind = Enum.Parse<NeowOptionKind>(optionReader.GetString(4)),
                Title = optionReader.IsDBNull(5) ? null : optionReader.GetString(5),
                Description = optionReader.IsDBNull(6) ? null : optionReader.GetString(6),
                Note = optionReader.IsDBNull(7) ? null : optionReader.GetString(7),
                Details = details
            }));
        }

        return rows
            .OrderBy(row => row.Index)
            .Select(row => row.Option)
            .ToList();
    }

    private static IReadOnlyList<RewardDetail> LoadAct1Details(SqliteConnection connection, long runId, int optionIndex)
    {
        using var detailCommand = connection.CreateCommand();
        detailCommand.CommandText =
            """
            SELECT detail_type, label, value_text, model_id, amount
            FROM act1_reward_details
            WHERE run_id = $run_id AND option_index = $option_index
            ORDER BY detail_index;
            """;
        detailCommand.Parameters.AddWithValue("$run_id", runId);
        detailCommand.Parameters.AddWithValue("$option_index", optionIndex);
        using var detailReader = detailCommand.ExecuteReader();
        var details = new List<RewardDetail>();
        while (detailReader.Read())
        {
            details.Add(new RewardDetail(
                Enum.Parse<RewardDetailType>(detailReader.GetString(0)),
                detailReader.GetString(1),
                detailReader.GetString(2),
                detailReader.IsDBNull(3) ? null : detailReader.GetString(3),
                detailReader.IsDBNull(4) ? null : detailReader.GetInt32(4)));
        }

        return details;
    }

    private static IReadOnlyList<Sts2ActPreview> LoadAncientActs(SqliteConnection connection, long runId)
    {
        using var actCommand = connection.CreateCommand();
        actCommand.CommandText =
            """
            SELECT act_number, ancient_id, ancient_name
            FROM act_ancients
            WHERE run_id = $run_id
            ORDER BY act_number;
            """;
        actCommand.Parameters.AddWithValue("$run_id", runId);
        using var actReader = actCommand.ExecuteReader();
        var acts = new List<Sts2ActPreview>();
        while (actReader.Read())
        {
            var actNumber = actReader.GetInt32(0);
            var act = new Sts2ActPreview
            {
                ActNumber = actNumber,
                AncientId = actReader.IsDBNull(1) ? null : actReader.GetString(1),
                AncientName = actReader.IsDBNull(2) ? null : actReader.GetString(2)
            };

            foreach (var option in LoadAncientOptions(connection, runId, actNumber))
            {
                act.AncientOptions.Add(option);
            }

            acts.Add(act);
        }

        return acts;
    }

    private static IReadOnlyList<Sts2AncientOption> LoadAncientOptions(SqliteConnection connection, long runId, int actNumber)
    {
        using var optionCommand = connection.CreateCommand();
        optionCommand.CommandText =
            """
            SELECT option_id, title, description, relic_id, note, was_chosen
            FROM act_ancient_options
            WHERE run_id = $run_id AND act_number = $act_number
            ORDER BY option_index;
            """;
        optionCommand.Parameters.AddWithValue("$run_id", runId);
        optionCommand.Parameters.AddWithValue("$act_number", actNumber);
        using var optionReader = optionCommand.ExecuteReader();
        var options = new List<Sts2AncientOption>();
        while (optionReader.Read())
        {
            options.Add(new Sts2AncientOption
            {
                OptionId = optionReader.GetString(0),
                Title = optionReader.IsDBNull(1) ? null : optionReader.GetString(1),
                Description = optionReader.IsDBNull(2) ? null : optionReader.GetString(2),
                RelicId = optionReader.IsDBNull(3) ? null : optionReader.GetString(3),
                Note = optionReader.IsDBNull(4) ? null : optionReader.GetString(4),
                WasChosen = !optionReader.IsDBNull(5) && optionReader.GetInt32(5) != 0
            });
        }

        return options;
    }

    internal static class SeedOrderHelper
    {
        private const string Alphabet = "0123456789ABCDEFGHJKLMNPQRSTUVWXYZ";

        public static long ToOrderValue(string seedText)
        {
            if (string.IsNullOrWhiteSpace(seedText))
            {
                return 0;
            }

            ulong value = 0;
            foreach (var ch in seedText.Trim().ToUpperInvariant())
            {
                var index = Alphabet.IndexOf(ch);
                if (index < 0)
                {
                    continue;
                }

                value = checked(value * (ulong)Alphabet.Length + (uint)index);
            }

            return unchecked((long)value);
        }
    }
}
