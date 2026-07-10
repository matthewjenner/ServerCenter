using ServerCenter.Core.Identity;

namespace ServerCenter.Controller.Endpoints;

// The bootstrap enrollment endpoint. Deliberately NOT client-cert authenticated (the agent has
// no cert yet - that is the chicken-and-egg this solves); it is gated by the one-time bootstrap
// token instead. Returns the agent's issued cert + key + the CA cert. The token and the CA
// fingerprint are delivered to the node out-of-band (cloud-init / the last SSH).
public static class EnrollmentEndpoint
{
    public static void MapEnrollment(this WebApplication app)
    {
        app.MapPost("/enroll", async (EnrollRequest request, IAgentTrustProvider trust, CancellationToken ct) =>
        {
            try
            {
                var result = await trust.EnrollAsync(new EnrollmentRequest(request.DisplayName, request.Token), ct);
                return Results.Ok(new EnrollResponse(
                    result.AgentId, result.CertPem, result.PrivateKeyPem, result.CaCertPem, result.CertFingerprint));
            }
            catch (InvalidOperationException ex)
            {
                // Invalid / used / expired token, or CA missing.
                return Results.Problem(ex.Message, statusCode: StatusCodes.Status401Unauthorized);
            }
        });
    }
}

public sealed record EnrollRequest(string DisplayName, string Token);

public sealed record EnrollResponse(
    string AgentId, string CertPem, string PrivateKeyPem, string CaCertPem, string Fingerprint);
