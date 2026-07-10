using System.Text.Json;
using System.Text.Json.Serialization;

namespace ServerCenter.Core.Games;

// The canonical JSON dialect for a stored game descriptor (game_descriptor.body_json). camelCase
// properties, null fields omitted, enum values as lowercase tokens ("kv", "ini") matching the
// hand-authored schema in the brief. Integer enum values are rejected on read so a bad format token
// fails loudly instead of binding to Kv.
public static class GameDescriptorSerializer
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };

    public static string Serialize(GameDescriptor descriptor) => JsonSerializer.Serialize(descriptor, Options);

    public static GameDescriptor Deserialize(string json) =>
        JsonSerializer.Deserialize<GameDescriptor>(json, Options)
        ?? throw new JsonException("game descriptor body deserialized to null");
}
