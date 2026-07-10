namespace ServerCenter.Controller.Persistence;

// Schema migration V1: the job-spine + identity tables from phase-0-contracts.md 2.4. The
// class tables (game_descriptor / update_policy / build_recipe / server_instance) and
// backup_snapshot land in their feature ships as later migrations, to avoid dead schema now.
internal static class SchemaV1
{
    public const long Version = 1;

    public const string Ddl = """
        CREATE TABLE agent_identity (
            id           TEXT PRIMARY KEY,
            display_name TEXT NOT NULL,
            cert_fpr     TEXT NOT NULL,
            status       TEXT NOT NULL,       -- pending | active | revoked
            enrolled_at  INTEGER NOT NULL,
            rotated_at   INTEGER,
            revoked_at   INTEGER
        );

        CREATE TABLE node (
            id             TEXT PRIMARY KEY,
            agent_id       TEXT REFERENCES agent_identity(id),
            kind           TEXT NOT NULL,      -- guest | host
            hostname       TEXT,
            os_family      TEXT,
            lifecycle      TEXT NOT NULL,      -- provisioning | managed | decommissioned
            libvirt_domain TEXT,
            created_at     INTEGER NOT NULL
        );
        CREATE INDEX ix_node_agent ON node(agent_id);

        CREATE TABLE job (
            id             TEXT PRIMARY KEY,
            node_id        TEXT NOT NULL REFERENCES node(id),
            type           TEXT NOT NULL,
            params_json    TEXT NOT NULL,
            state          TEXT NOT NULL,      -- queued|running|succeeded|failed|timedout|cancelled
            progress_pct   INTEGER,
            progress_note  TEXT,
            cancellable    INTEGER NOT NULL,
            requeueable    INTEGER NOT NULL,
            last_acked_seq INTEGER NOT NULL DEFAULT 0,
            created_at     INTEGER NOT NULL,
            started_at     INTEGER,
            terminal_at    INTEGER,            -- set once, on terminal entry
            fail_reason    TEXT,
            correlation_id TEXT
        );
        CREATE INDEX ix_job_node_state ON job(node_id, state);
        CREATE INDEX ix_job_open ON job(state) WHERE terminal_at IS NULL;

        CREATE TABLE job_log (
            job_id     TEXT NOT NULL REFERENCES job(id),
            seq        INTEGER NOT NULL,
            ts_unix_ms INTEGER NOT NULL,
            stream     TEXT NOT NULL,          -- stdout | stderr | note
            line       TEXT NOT NULL,
            PRIMARY KEY (job_id, seq)
        );
        """;
}
