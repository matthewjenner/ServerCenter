namespace ServerCenter.Controller.Persistence;

// Schema migration V2: the controller's private CA and one-time bootstrap tokens (brief 3.8).
// The CA private key lives here as precious state (backed up to S3). Encryption-at-rest of the
// key material is a later hardening; for now it relies on the DB file's own protection.
internal static class SchemaV2
{
    public const string Ddl = """
        CREATE TABLE controller_ca (
            id         INTEGER PRIMARY KEY CHECK (id = 1),   -- singleton row
            cert_pem   TEXT NOT NULL,
            key_pem    TEXT NOT NULL,
            created_at INTEGER NOT NULL
        );

        CREATE TABLE bootstrap_token (
            token_sha256 TEXT PRIMARY KEY,   -- only the hash is stored
            display_name TEXT NOT NULL,
            expires_at   INTEGER NOT NULL,
            used_at      INTEGER             -- one-time: set on consumption
        );
        """;
}
