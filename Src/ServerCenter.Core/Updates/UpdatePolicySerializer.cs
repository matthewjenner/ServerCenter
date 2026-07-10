using System.Text.Json;

namespace ServerCenter.Core.Updates;

// Serializes a stored UpdatePolicy (update_policy.body_json) in the canonical update dialect
// (UpdateJson): kebab-case enum tokens, camelCase properties. The body is hand-authorable and
// decoupled from C# member-name casing - the same discipline the job spine uses for its state text.
public static class UpdatePolicySerializer
{
    public static string Serialize(UpdatePolicy policy) => JsonSerializer.Serialize(policy, UpdateJson.Options);

    public static UpdatePolicy Deserialize(string json) =>
        JsonSerializer.Deserialize<UpdatePolicy>(json, UpdateJson.Options)
        ?? throw new JsonException("update policy body deserialized to null");
}
