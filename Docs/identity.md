# Identity, trust, and auth

How an agent proves who it is and how the controller decides to trust it. The invariant: **the
controller owns identity** - it mints, stores, and pins every per-agent identity, so the root of
trust is centralized even though the work is decentralized. It sits behind `IAgentTrustProvider` in
Core so a future untrusted-node model can swap in. ASCII punctuation only (house rule).

## Current state: mTLS, wired and enforced

- **The controller is its own private CA** (`CertificateAuthority`, RSA). It mints per-agent client
  certificates and its own startup TLS server certificate, and pins each agent's sha256 fingerprint.
  Persisted in SQLite (schema V2: `controller_ca`, `bootstrap_token`; per-agent rows in
  `agent_identity`).
- **Enrollment is token-gated.** `POST /enroll` takes a one-time, expiring bootstrap token (only its
  hash is stored) and needs no client certificate; it returns the agent's certificate, key, and the
  CA bundle. The operator mints the token via `POST /enroll-token`
  (`CreateBootstrapTokenAsync`, TTL default 60 minutes, clamped to 24 hours), surfaced in the UI
  Settings tab as "Enroll a new node".
- **Transport.** The controller runs Kestrel with `ClientCertificateMode.AllowCertificate`.
  `AgentAuthorizer` enforces per connection inside `AgentLinkService`: the certificate CN must equal
  the claimed agent id **and** the fingerprint must verify (pending or active, not revoked). A
  successful connection flips pending to active. Unauthorized or certificate-less connections get
  `Goodbye(Revoked)`.
- **Agent side.** `EnrollmentClient` + `AgentCertStore` + the Program bootstrap enroll once using
  `SERVERCENTER_ENROLL_TOKEN`, persist into a gitignored `agent-identity/` directory, and dial mTLS
  through `GrpcTransportConnector`, presenting the client certificate and validating the server
  against the CA (not against the hostname - so the server certificate subject stays `localhost`
  even for remote agents).

Proven by a real-socket integration test: a real Kestrel on an ephemeral `127.0.0.1:0` port, an
enrolled agent connecting and heartbeating, and a certificate-less connection being rejected.

### The enforcement switch

Enforcement is gated by configuration `Security:RequireClientCertificate`, **default true**.
In-process integration tests built on `WebApplicationFactory`/`TestServer` set it **false**, because
TestServer has no TLS at all; those tests exercise the plaintext h2c development path. So "mTLS off"
happens only in that dev/test mode, never by default. See `Docs/testing.md`.

## Why the agent runs as root

The agent unit (`Deploy/servercenter-agent.service`) runs as **root**, deliberately.

It previously ran `User=servercenter` with `NoNewPrivileges=true`, `ProtectSystem=strict`,
`ProtectHome=true`, and `PrivateTmp=true`. On live hardware every `apt` update job failed with
`Could not open lock file /var/lib/apt/lists/lock (13: Permission denied)` and
`Read-only file system (30)`: the agent was not root, and `/usr` + `/var` were mounted read-only.
VM start/stop jobs still worked, because those run on the **controller** via libvirt, not on the
agent - which is what made the failure look selective.

This is fundamental, not a tuning problem. The agent's whole job is to manage the node: apt
update/upgrade, `systemctl` service control, SteamCMD installs into `/opt`, host reboot, writing
configuration files. A blanket unprivileged + strict-sandbox profile is incompatible with that job,
and `NoNewPrivileges` blocks sudo/setuid regardless. Running as root is the standard posture for an
infrastructure/config-management agent (Ansible and friends do the same).

Consequently `Deploy/install.sh` and `Deploy/update/servercenter-agent-update.sh` do **not** create a
`servercenter` user or `chown` to it - a leftover chown would hard-fail the updater on nodes
installed after the user was dropped.

Optional future hardening (not a blocker): layer back **targeted** confinement - an explicit
`ReadWritePaths=` for the paths it needs plus a specific `CapabilityBoundingSet=` - instead of the
blanket strict profile.

## Known gaps

- **CA private key is plaintext PEM in SQLite.** It is precious and backed up, but encryption at
  rest is not implemented.
- **Enrollment server-trust is TOFU.** The agent accepts any server certificate during `/enroll`.
  Production should pin the CA fingerprint delivered out of band.
- **Certificate rotation is not pushed.** `RotateAsync` returns a new bundle, but delivery to a
  running agent over the wire is not implemented.
- **The operator surface is not authenticated.** The enroll-token mint endpoint, the FleetView and
  JobView gRPC services, and the jobs/admin HTTP endpoints have no operator auth yet; the UI
  validates the server certificate trust-on-first-use. Operator auth is deferred.

## Planned: pending-to-approve onboarding

**Decided direction, not yet built.** Onboarding moves to a **pending -> approve gate**: an agent
that connects lands in a pending ("unaccepted") list and does nothing until an operator approves it
in the UI; on approval the controller mints and pins its identity and the node becomes managed. This
is the SaltStack/Puppet/Teleport unaccepted-key model.

Chosen over the two alternatives: a pre-shared bootstrap token (the current design - workable, but
onboarding still needs an out-of-band secret), and controller-driven SSH install (rejected - it
would give the controller SSH/root to every node, far too large a surface).

What it replaces: today there is no approval gate. The plaintext/http bring-up path accepts any
agent that dials in (trusted-LAN only), and the mTLS path has no "connected but unenrolled" state
because a valid certificate is required to connect at all.

Being contracts-first, this needs a contract before code: an agent lifecycle state
(pending/approved/revoked), an approve endpoint, and the UI surface. Note that the
controller-distributed agent auto-update was shipped intentionally **against the current open model**
(it serves any connected agent over plaintext); restricting it to approved agents only is a retrofit
once this gate lands. See `Docs/build-and-update.md`.
