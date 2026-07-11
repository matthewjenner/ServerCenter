using ServerCenter.Controller.Persistence;
using ServerCenter.Core.Updates;

namespace ServerCenter.Controller.Services;

// Seeds ready-to-use update policies on startup so the operator's policy picker is never empty and the
// common case (keep a Linux node's apt packages current) works with zero setup. Idempotent: a policy
// id that already exists is left as-is, so an operator edit is never overwritten.
public static class DefaultPolicies
{
    private static readonly string[] Policies =
    [
        // "apt": apt update/upgrade in place; reboot only if the update requires it; no confirmation.
        "{\"id\":\"apt\",\"version\":1,\"what\":{\"provider\":\"apt\"},\"how\":\"in-place\"," +
        "\"when\":{\"mode\":\"manual\"},\"reboot\":\"if-required\",\"preflight\":[\"notify\"],\"approval\":\"auto\"}"
    ];

    public static async Task EnsureAsync(UpdatePolicyRepository repository, TimeProvider clock, CancellationToken ct)
    {
        foreach (string json in Policies)
        {
            UpdatePolicy policy = UpdatePolicySerializer.Deserialize(json);
            if (await repository.GetLatestAsync(policy.Id, ct) is null)
            {
                await repository.InsertAsync(policy, clock.GetUtcNow().ToUnixTimeMilliseconds(), ct);
            }
        }
    }
}
