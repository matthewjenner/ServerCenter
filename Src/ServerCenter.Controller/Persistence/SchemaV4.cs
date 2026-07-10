namespace ServerCenter.Controller.Persistence;

// Schema migration V4: the game-server declarative surface (Phase 5). game_descriptor is a versioned
// CLASS catalog (like update_policy); server_instance is the INSTANCE side - a concrete running
// server that pins the exact descriptor/recipe/policy versions governing it plus its params. Tables
// land with the feature that uses them (avoid dead schema); recipe/policy columns are here for the
// FK shape but are populated by Phases 4/7.
internal static class SchemaV4
{
    public const string Ddl = """
        CREATE TABLE game_descriptor (
            id         TEXT NOT NULL,
            version    INTEGER NOT NULL,
            body_json  TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            PRIMARY KEY (id, version)
        );

        CREATE TABLE server_instance (
            id                   TEXT PRIMARY KEY,
            node_id              TEXT NOT NULL REFERENCES node(id),
            descriptor_id        TEXT,
            descriptor_version   INTEGER,
            recipe_id            TEXT,
            recipe_version       INTEGER,
            policy_id            TEXT,
            policy_version       INTEGER,
            instance_params_json TEXT NOT NULL,
            created_at           INTEGER NOT NULL
        );

        CREATE INDEX ix_server_instance_node ON server_instance(node_id);
        """;
}
