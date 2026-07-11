using ServerCenter.Controller.Services;
using ServerCenter.Core.Identity;

namespace ServerCenter.Controller.Endpoints;

// The bootstrap enrollment endpoints.
//
// POST /enroll-token is the OPERATOR side: mint a one-time, short-TTL bootstrap token for a new node.
// Only its hash is stored; the plaintext is returned once, to be delivered to the node out-of-band
// (cloud-init / the last SSH). Operator auth is deferred here, same posture as the other operator
// endpoints (plaintext bring-up) - this closes the "no token-mint endpoint" end-to-end gap.
//
// POST /enroll is the AGENT side: deliberately NOT client-cert authenticated (the agent has no cert
// yet - that is the chicken-and-egg this solves); it is gated by the one-time token instead, and
// returns the agent's issued cert + key + the CA cert.
public static class EnrollmentEndpoint
{
    // Bootstrap tokens are short-lived by nature; clamp the requested TTL so a stray value can't mint
    // a long-lived credential. Default one hour if unspecified.
    private const int DefaultTtlMinutes = 60;
    private const int MaxTtlMinutes = 24 * 60;

    public static void MapEnrollment(this WebApplication app)
    {
        app.MapPost("/enroll-token",
            async (EnrollTokenRequest request, ControllerOwnedTrustProvider trust, TimeProvider clock, CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(request.DisplayName))
                {
                    return Results.BadRequest(new { error = "displayName is required" });
                }

                int ttlMinutes = Math.Clamp(request.TtlMinutes ?? DefaultTtlMinutes, 1, MaxTtlMinutes);
                TimeSpan ttl = TimeSpan.FromMinutes(ttlMinutes);
                string token = await trust.CreateBootstrapTokenAsync(request.DisplayName.Trim(), ttl, ct);
                long expiresAt = clock.GetUtcNow().Add(ttl).ToUnixTimeMilliseconds();
                return Results.Ok(new EnrollTokenResponse(token, request.DisplayName.Trim(), expiresAt, ttlMinutes));
            });

        app.MapPost("/enroll", async (EnrollRequest request, IAgentTrustProvider trust, CancellationToken ct) =>
        {
            try
            {
                EnrollmentResult result = await trust.EnrollAsync(new EnrollmentRequest(request.DisplayName, request.Token), ct);
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

// ttlMinutes is optional; the endpoint defaults + clamps it.
public sealed record EnrollTokenRequest(string DisplayName, int? TtlMinutes);

public sealed record EnrollTokenResponse(string Token, string DisplayName, long ExpiresAtUnixMs, int TtlMinutes);

public sealed record EnrollRequest(string DisplayName, string Token);

public sealed record EnrollResponse(
    string AgentId, string CertPem, string PrivateKeyPem, string CaCertPem, string Fingerprint);
