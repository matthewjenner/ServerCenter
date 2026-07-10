using System.Globalization;
using System.Text.Json;

namespace ServerCenter.Primitives.ConfigTemplating;

// Flattens a server instance's params JSON into dotted-path string values for the config-template
// renderer: {"ports":{"game":27015}} -> {"ports.game":"27015"}. This is the class-vs-instance seam -
// a shared descriptor template (the class) rendered with per-server params (the instance). Pairs with
// ConfigTemplateRenderer, which resolves {{ports.game}} against the flattened map.
public static class InstanceParamsResolver
{
    public static IReadOnlyDictionary<string, string> Flatten(string instanceParamsJson)
    {
        using JsonDocument document = JsonDocument.Parse(instanceParamsJson);
        Dictionary<string, string> values = new Dictionary<string, string>();
        Walk(prefix: null, document.RootElement, values);
        return values;
    }

    private static void Walk(string? prefix, JsonElement element, Dictionary<string, string> into)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    Walk(prefix is null ? property.Name : $"{prefix}.{property.Name}", property.Value, into);
                }

                break;

            case JsonValueKind.Array:
                int index = 0;
                foreach (JsonElement item in element.EnumerateArray())
                {
                    Walk(prefix is null ? index.ToString(CultureInfo.InvariantCulture) : $"{prefix}.{index}", item, into);
                    index++;
                }

                break;

            default:
                // A leaf. A bare scalar at the root has no key to bind to, so it is skipped.
                if (prefix is not null)
                {
                    into[prefix] = Scalar(element);
                }

                break;
        }
    }

    private static string Scalar(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? string.Empty,
        JsonValueKind.Number => element.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => string.Empty,
        _ => element.GetRawText()
    };
}
