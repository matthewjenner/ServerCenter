using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerCenter.Core.Updates;

// The canonical JSON shape for a stored UpdatePolicy (update_policy.body_json). Enums serialize as
// stable kebab-case tokens ("stop-update-start", "if-required", "require-confirmation") so the body
// is human-authorable and decoupled from C# member-name casing - the same discipline the job spine
// uses for its state text. camelCase properties match the brief's schema (phase-0-contracts.md 5).
// Integer enum values are rejected on read so a typo fails loudly instead of binding to 0.
public static class UpdatePolicySerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) }
    };

    public static string Serialize(UpdatePolicy policy) => JsonSerializer.Serialize(policy, Options);

    public static UpdatePolicy Deserialize(string json) =>
        JsonSerializer.Deserialize<UpdatePolicy>(json, Options)
        ?? throw new JsonException("update policy body deserialized to null");
}
