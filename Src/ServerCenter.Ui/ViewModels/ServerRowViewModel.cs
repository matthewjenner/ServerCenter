using ServerCenter.Ui.Services;

namespace ServerCenter.Ui.ViewModels;

// One row in the Servers read view: a defined server instance and the class definitions it is bound
// to (descriptor / recipe / policy shown as id@version, or "-" when unbound).
public sealed class ServerRowViewModel
{
    public ServerRowViewModel(ServerInstanceRow row)
    {
        Id = row.Id;
        Node = row.NodeId;
        Descriptor = Ref(row.DescriptorId, row.DescriptorVersion);
        Recipe = Ref(row.RecipeId, row.RecipeVersion);
        Policy = Ref(row.PolicyId, row.PolicyVersion);
    }

    public string Id { get; }

    public string Node { get; }

    public string Descriptor { get; }

    public string Recipe { get; }

    public string Policy { get; }

    private static string Ref(string? id, int? version) =>
        string.IsNullOrEmpty(id) ? "-" : version is null ? id : $"{id}@{version}";
}
