namespace ServerCenter.Primitives.ConfigTemplating;

// Builds the token map used to render a server instance's per-instance strings (install dir, systemd
// unit name, ExecStart, config-file paths) so N instances of the SAME game descriptor/recipe never
// collide on disk or on unit names. It is the flattened instance params (InstanceParamsResolver) PLUS
// a reserved namespace the operator can reference in a descriptor/recipe authored once:
//   instance.id   - the ServerInstance id (filesystem/systemd-safe; the uniqueness key)
//   instance.name - a friendly name (the params' "name", falling back to the id)
//   node.id       - the node the instance runs on
//   instance.dir  - the rendered install dir; NOT set here (it depends on rendering the install-dir
//                   template first) - the caller adds it via the InstanceDirKey once known.
// This is the data-over-code seam: multi-instance is absorbed by templating, not per-game code.
public static class InstanceContext
{
    public const string InstanceIdKey = "instance.id";
    public const string InstanceNameKey = "instance.name";
    public const string InstanceDirKey = "instance.dir";
    public const string NodeIdKey = "node.id";

    // A mutable map (so the caller can add instance.dir after rendering the install dir).
    public static Dictionary<string, string> Build(string instanceId, string nodeId, string instanceParamsJson)
    {
        Dictionary<string, string> tokens = new Dictionary<string, string>(InstanceParamsResolver.Flatten(instanceParamsJson));
        tokens[InstanceIdKey] = instanceId;
        tokens[NodeIdKey] = nodeId;
        if (!tokens.ContainsKey(InstanceNameKey))
        {
            tokens[InstanceNameKey] = tokens.TryGetValue("name", out string? name) && !string.IsNullOrWhiteSpace(name)
                ? name
                : instanceId;
        }

        return tokens;
    }
}
