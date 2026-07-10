using Microsoft.Data.Sqlite;
using ServerCenter.Core.Jobs;

namespace ServerCenter.Controller.Persistence;

// Persists the job spine (brief 3.6): jobs survive controller restart and power resync and the
// "what happened last Tuesday" view. State is stored as stable text, decoupled from the enum's
// name casing.
public sealed class JobRepository(ServerCenterDatabase database)
{
    // Unqualified column list for INSERT.
    private const string InsertColumns =
        "id, node_id, type, params_json, state, progress_pct, progress_note, cancellable, " +
        "requeueable, last_acked_seq, created_at, started_at, terminal_at, fail_reason, correlation_id";

    // job-qualified column list for SELECT (the open-jobs query joins node, which also has an
    // 'id' column - unqualified would be ambiguous). Order must match Map().
    private const string SelectColumns =
        "job.id, job.node_id, job.type, job.params_json, job.state, job.progress_pct, job.progress_note, " +
        "job.cancellable, job.requeueable, job.last_acked_seq, job.created_at, job.started_at, " +
        "job.terminal_at, job.fail_reason, job.correlation_id";

    public async Task InsertAsync(Job job, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            $"INSERT INTO job ({InsertColumns}) VALUES " +
            "(@id, @node, @type, @params, @state, @pct, @note, @cancellable, @requeueable, " +
            "@seq, @created, @started, @terminal, @fail, @corr);";
        cmd.Parameters.AddWithValue("@id", job.Id);
        cmd.Parameters.AddWithValue("@node", job.NodeId);
        cmd.Parameters.AddWithValue("@type", job.Type);
        cmd.Parameters.AddWithValue("@params", job.ParamsJson);
        cmd.Parameters.AddWithValue("@state", StateText(job.State));
        cmd.Parameters.AddWithValue("@pct", (object?)job.ProgressPct ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@note", (object?)job.ProgressNote ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@cancellable", job.Cancellable ? 1 : 0);
        cmd.Parameters.AddWithValue("@requeueable", job.Requeueable ? 1 : 0);
        cmd.Parameters.AddWithValue("@seq", job.LastAckedSeq);
        cmd.Parameters.AddWithValue("@created", job.CreatedAtUnixMs);
        cmd.Parameters.AddWithValue("@started", (object?)job.StartedAtUnixMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@terminal", (object?)job.TerminalAtUnixMs ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@fail", (object?)job.FailReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@corr", (object?)job.CorrelationId ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task<Job?> GetAsync(string jobId, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM job WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", jobId);
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? Map(reader) : null;
    }

    // Open (non-terminal) jobs for an agent, joined through its node. Powers the resync
    // handshake (phase-0-contracts.md 2.3).
    public async Task<IReadOnlyList<Job>> GetOpenJobsForAgentAsync(string agentId, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            $"SELECT {SelectColumns} FROM job JOIN node ON node.id = job.node_id " +
            "WHERE node.agent_id = @agentId AND job.terminal_at IS NULL;";
        cmd.Parameters.AddWithValue("@agentId", agentId);

        List<Job> jobs = new List<Job>();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            jobs.Add(Map(reader));
        }

        return jobs;
    }

    // Recent jobs across the fleet, newest first, for the operator job view.
    public async Task<IReadOnlyList<Job>> ListRecentJobsAsync(int limit, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = $"SELECT {SelectColumns} FROM job ORDER BY created_at DESC LIMIT @limit;";
        cmd.Parameters.AddWithValue("@limit", limit);

        List<Job> jobs = new List<Job>();
        await using SqliteDataReader reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            jobs.Add(Map(reader));
        }

        return jobs;
    }

    public async Task UpdateStateAsync(
        string jobId, JobState state, string? failReason, long? terminalAtUnixMs, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        // Requeue (back to queued) clears the terminal and start markers so it can run afresh.
        cmd.CommandText =
            "UPDATE job SET state = @state, fail_reason = @fail, terminal_at = @terminal, " +
            "started_at = CASE WHEN @state = 'queued' THEN NULL ELSE started_at END " +
            "WHERE id = @id;";
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue("@state", StateText(state));
        cmd.Parameters.AddWithValue("@fail", (object?)failReason ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@terminal", (object?)terminalAtUnixMs ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Applies a streamed progress tick: a queued job becomes running (stamping started_at), and
    // progress pct/note update. Guarded on non-terminal so a late tick cannot revert a finished job.
    public async Task ApplyProgressAsync(string jobId, int? pct, string? note, long nowUnixMs, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            "UPDATE job SET " +
            "state = CASE WHEN state = 'queued' THEN 'running' ELSE state END, " +
            "started_at = COALESCE(started_at, @now), " +
            "progress_pct = @pct, progress_note = @note " +
            "WHERE id = @id AND terminal_at IS NULL;";
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue("@now", nowUnixMs);
        cmd.Parameters.AddWithValue("@pct", (object?)pct ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@note", (object?)note ?? DBNull.Value);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task AppendLogAsync(
        string jobId, long seq, LogStream stream, string line, long tsUnixMs, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText =
            "INSERT INTO job_log (job_id, seq, ts_unix_ms, stream, line) " +
            "VALUES (@job, @seq, @ts, @stream, @line);";
        cmd.Parameters.AddWithValue("@job", jobId);
        cmd.Parameters.AddWithValue("@seq", seq);
        cmd.Parameters.AddWithValue("@ts", tsUnixMs);
        cmd.Parameters.AddWithValue("@stream", StreamText(stream));
        cmd.Parameters.AddWithValue("@line", line);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // Move the ack watermark forward only (bounds the agent's replay buffer, 2.3).
    public async Task AckLogAsync(string jobId, long seq, CancellationToken ct)
    {
        await using SqliteConnection connection = await database.OpenConnectionAsync(ct);
        await using SqliteCommand cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE job SET last_acked_seq = @seq WHERE id = @id AND @seq > last_acked_seq;";
        cmd.Parameters.AddWithValue("@id", jobId);
        cmd.Parameters.AddWithValue("@seq", seq);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static Job Map(SqliteDataReader r) => new()
    {
        Id = r.GetString(0),
        NodeId = r.GetString(1),
        Type = r.GetString(2),
        ParamsJson = r.GetString(3),
        State = ParseState(r.GetString(4)),
        ProgressPct = r.IsDBNull(5) ? null : r.GetInt32(5),
        ProgressNote = r.IsDBNull(6) ? null : r.GetString(6),
        Cancellable = r.GetInt64(7) != 0,
        Requeueable = r.GetInt64(8) != 0,
        LastAckedSeq = r.GetInt64(9),
        CreatedAtUnixMs = r.GetInt64(10),
        StartedAtUnixMs = r.IsDBNull(11) ? null : r.GetInt64(11),
        TerminalAtUnixMs = r.IsDBNull(12) ? null : r.GetInt64(12),
        FailReason = r.IsDBNull(13) ? null : r.GetString(13),
        CorrelationId = r.IsDBNull(14) ? null : r.GetString(14)
    };

    private static string StateText(JobState state) => state switch
    {
        JobState.Queued => "queued",
        JobState.Running => "running",
        JobState.Succeeded => "succeeded",
        JobState.Failed => "failed",
        JobState.TimedOut => "timedout",
        JobState.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(state), state, null)
    };

    private static JobState ParseState(string text) => text switch
    {
        "queued" => JobState.Queued,
        "running" => JobState.Running,
        "succeeded" => JobState.Succeeded,
        "failed" => JobState.Failed,
        "timedout" => JobState.TimedOut,
        "cancelled" => JobState.Cancelled,
        _ => throw new InvalidOperationException($"unknown job state '{text}'")
    };

    private static string StreamText(LogStream stream) => stream switch
    {
        LogStream.Stdout => "stdout",
        LogStream.Stderr => "stderr",
        LogStream.Note => "note",
        _ => throw new ArgumentOutOfRangeException(nameof(stream), stream, null)
    };
}
