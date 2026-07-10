# The controller container. Runs on the hypervisor host with the local libvirt socket mounted
# (VM-lifecycle plane is controller-driven, brief 3.2/3.3). Build from the repo root:
#   docker build -f Deploy/controller/Dockerfile -t servercenter-controller .

# ---- build ----
# .NET 10 default images are Ubuntu 24.04 based (Debian was dropped in .NET 10).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish Src/ServerCenter.Controller/ServerCenter.Controller.csproj -c Release -o /app

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

# Precious state (SQLite DB) + config templates live on mounted volumes; libvirt is enabled since
# the socket is mounted in. mTLS is on by default (Security__RequireClientCertificate unset = true).
ENV Database__Path=/data/servercenter.db \
    Templates__Root=/templates \
    Libvirt__Enabled=true

# 5443 = mTLS (default); 5080 = plaintext h2c (bring-up / dev). Which one binds depends on
# Security__RequireClientCertificate (true -> 5443, false -> 5080). Publish the matching port in compose.
EXPOSE 5443 5080
ENTRYPOINT ["dotnet", "ServerCenter.Controller.dll"]
