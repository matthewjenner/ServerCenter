namespace ServerCenter.Controller.Persistence;

// Schema migration V5: build_recipe, the third declarative CLASS catalog (after update_policy and
// game_descriptor), versioned JSON keyed by (id, version). A server_instance already pins recipe_id/
// recipe_version (V4), so a built server always reconstructs which recipe version made it.
internal static class SchemaV5
{
    public const string Ddl = """
        CREATE TABLE build_recipe (
            id         TEXT NOT NULL,
            version    INTEGER NOT NULL,
            body_json  TEXT NOT NULL,
            created_at INTEGER NOT NULL,
            PRIMARY KEY (id, version)
        );
        """;
}
