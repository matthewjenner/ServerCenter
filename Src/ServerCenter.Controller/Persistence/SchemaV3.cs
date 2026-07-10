namespace ServerCenter.Controller.Persistence;

// Schema migration V3: the first declarative class table lands with the feature that uses it
// (avoid dead schema). update_policy stores versioned policies as validated JSON keyed by
// (id, version), so a running server pins the exact policy version it was governed by and history
// reconstructs exactly (phase-0-contracts.md 2.4, 5). The descriptor/recipe/instance tables follow
// with Phases 5 and 7.
internal static class SchemaV3
{
    public const string Ddl = """
        CREATE TABLE update_policy (
            id         TEXT NOT NULL,
            version    INTEGER NOT NULL,
            body_json  TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            PRIMARY KEY (id, version)
        );
        """;
}
