using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using ServerCenter.Core.Connection;

namespace ServerCenter.Agent;

// Prepares the connector + identity from options. Over https it runs the mTLS bootstrap (enroll
// once with the one-time token, persist, then present the client cert); over http it runs the
// plaintext dev path. Identical for host and guests - only NodeKind differs.
public static class AgentBootstrap
{
    public static async Task<(GrpcTransportConnector Connector, AgentIdentity Identity)> PrepareAsync(
        AgentOptions options, ILogger logger, CancellationToken ct)
    {
        var osFamily = OperatingSystem.IsWindows() ? "windows" : "linux";
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();

        if (options.ControllerAddress.StartsWith("https", StringComparison.OrdinalIgnoreCase))
        {
            var store = new AgentCertStore(options.CertDirectory);
            if (!store.Exists)
            {
                if (string.IsNullOrEmpty(options.EnrollToken))
                {
                    throw new InvalidOperationException(
                        "No local identity and no SERVERCENTER_ENROLL_TOKEN set; cannot bootstrap mTLS. " +
                        "Obtain a one-time enrollment token from the controller, or use an http:// address for dev.");
                }

                logger.LogInformation("Enrolling '{DisplayName}' with {Address}", options.DisplayName, options.ControllerAddress);
                var bundle = await EnrollmentClient.EnrollAsync(
                    options.ControllerAddress, options.DisplayName, options.EnrollToken, ct);
                store.Save(bundle);
                logger.LogInformation("Enrolled as agent {AgentId}", bundle.AgentId);
            }

            var identity = new AgentIdentity(store.AgentId, "0.1.0", osFamily, arch, options.NodeKind);
            return (new GrpcTransportConnector(options.ControllerAddress, store.LoadTls()), identity);
        }

        // Plaintext dev: allow HTTP/2 over cleartext and use a fixed dev id (no enrollment).
        AppContext.SetSwitch("System.Net.Http.SocketsHttpHandler.Http2UnencryptedSupport", true);
        var devId = options.DevAgentId ?? "dev-agent";
        return (new GrpcTransportConnector(options.ControllerAddress),
            new AgentIdentity(devId, "0.1.0", osFamily, arch, options.NodeKind));
    }
}
