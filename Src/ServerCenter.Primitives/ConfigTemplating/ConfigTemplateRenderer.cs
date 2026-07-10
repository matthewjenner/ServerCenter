using System.Text.RegularExpressions;

namespace ServerCenter.Primitives.ConfigTemplating;

// The config-templating primitive (pure logic, no seam, directly unit-testable). Renders a
// template by substituting {{key}} tokens from instance params. Used by game config-gen AND
// build recipes. Format-specific writers (INI/JSON/KV/XML) layer on top of this later.
public static partial class ConfigTemplateRenderer
{
    [GeneratedRegex(@"\{\{\s*([\w.]+)\s*\}\}")]
    private static partial Regex TokenRegex();

    // Missing token is a hard error: a half-rendered config is worse than a clear failure.
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        return TokenRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (!values.TryGetValue(key, out var value))
            {
                throw new KeyNotFoundException($"Config template references unknown token '{key}'.");
            }

            return value;
        });
    }
}
