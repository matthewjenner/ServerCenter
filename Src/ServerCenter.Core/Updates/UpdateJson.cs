using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerCenter.Core.Updates;

// The one canonical JSON dialect for update-domain payloads: a stored policy body and a dispatched
// job's params. camelCase properties, kebab-case enum tokens ("stop-update-start", "if-required"),
// null fields omitted, integer enum values rejected on read (a typo fails loudly instead of binding
// to 0). Sharing one options instance keeps policy and job params speaking the same language.
public static class UpdateJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.KebabCaseLower, allowIntegerValues: false) }
    };
}
