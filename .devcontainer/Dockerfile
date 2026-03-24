FROM mcr.microsoft.com/devcontainers/dotnet:1-10.0

# Remove stale Yarn apt repo whose GPG key has expired, breaking apt-get update
RUN rm -f /etc/apt/sources.list.d/yarn.list \
    && apt-get clean \
    && apt-get update
