# The controller container. Runs on the hypervisor host with the local libvirt socket mounted
# (VM-lifecycle plane is controller-driven, brief 3.2/3.3). Build from the repo root:
#   docker build -f Deploy/controller/Dockerfile -t servercenter-controller .

# ---- build ----
# .NET 10 default images are Ubuntu 24.04 based (Debian was dropped in .NET 10).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Src/ServerCenter.Controller/ServerCenter.Controller.csproj -c Release -o /app

# Bake the agent self-contained bundles into the image so the controller can serve agent self-updates
# (agents pull from the controller, never GitHub). Same packaging the GitHub release uses, so the
# advertised version and the served payload always match the controller's own lockstep version.
RUN bash Scripts/publish-agent.sh linux-x64 \
    && bash Scripts/publish-agent.sh linux-arm64 \
    && mkdir -p /agent-bundles \
    && cp artifacts/agent/linux-x64/*.tar.gz   /agent-bundles/servercenter-agent-linux-x64.tar.gz \
    && cp artifacts/agent/linux-arm64/*.tar.gz /agent-bundles/servercenter-agent-linux-arm64.tar.gz

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

# virsh (libvirt-clients) so ILibvirtHost can drive the local libvirt over the mounted socket.
# root so the container can access the libvirt socket (a trusted controller on the hypervisor).
USER root
RUN apt-get update \
    && apt-get install -y --no-install-recommends libvirt-clients \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Agent self-update bundles served by GET /agent/bundle/{rid}.
COPY --from=build /agent-bundles /app/agent-bundles

# Precious state (SQLite DB) + config templates live on mounted volumes; libvirt is enabled since
# the socket is mounted in. mTLS is on by default (Security__RequireClientCertificate unset = true).
ENV Database__Path=/data/servercenter.db \
    Templates__Root=/templates \
    AgentBundles__Root=/app/agent-bundles \
    Libvirt__Enabled=true

# 5443 = mTLS (default); 5080 = plaintext h2c (bring-up / dev). Which one binds depends on
# Security__RequireClientCertificate (true -> 5443, false -> 5080). Publish the matching port in compose.
EXPOSE 5443 5080
ENTRYPOINT ["dotnet", "ServerCenter.Controller.dll"]
