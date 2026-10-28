# syntax=docker/dockerfile:1.7
# Part of DotNetSolutionKit (https://dotskit.sawking.tech/). MIT License, Copyright (c) 2025 Vladimir Savkin.
#
#
# One Dockerfile for every service. Build a service from the solution root:
#
#   docker build --build-arg SERVICE=<Service folder name> --build-arg GIT_SHA=<commit> -t <image> .
#
# The common stage depends only on src/common, src/capabilities and the build props, and the service stage
# only adds the service's own folder. A change in one service therefore leaves every other service's image
# untouched - same layers, same digest - and the shared code is compiled once per change to it, not once
# per service. GIT_SHA should be the last commit that touched the service's inputs, not the current
# one; otherwise every commit changes every image (see the deploy scripts).

ARG DOTNET_VERSION=8.0

# --- Common: restored and compiled once, reused by every service ---
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS common
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY Directory.Build.props version.json ./
COPY src/Directory.Packages.props src/
COPY src/package-versions/ src/package-versions/
COPY src/common/ src/common/
COPY src/capabilities/ src/capabilities/

RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    for project in src/common/*/*.csproj src/capabilities/*/*.csproj; do \
        [ -f "$project" ] || continue; \
        case "$project" in *.Tests.csproj) continue ;; esac; \
        dotnet build "$project" -c "$BUILD_CONFIGURATION"; \
    done

# --- Service: only this service's folder on top of the compiled Common ---
FROM common AS service
ARG BUILD_CONFIGURATION=Release
ARG SERVICE
RUN test -n "$SERVICE" || (echo "Pass --build-arg SERVICE=<service folder name>" && exit 1)

COPY src/services/${SERVICE}/ src/services/${SERVICE}/

RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish "src/services/${SERVICE}/${SERVICE}.API/${SERVICE}.API.csproj" \
        -c "$BUILD_CONFIGURATION" \
        -o /app/publish \
        -p:UseAppHost=false \
    && echo "${SERVICE}.API.dll" > /app/publish/.entrypoint

# --- Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS runtime

# curl serves the container healthcheck below; the aspnet image does not ship it.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
EXPOSE 8080

# A fixed uid, so a mounted volume (keys, certificates) has the same owner on every host.
RUN mkdir -p /app/https && chown -R 1000:1000 /app
COPY --chown=1000:1000 --from=service /app/publish .
USER 1000:1000

# The commit the service was built from; reported by /health. Set last, so it only changes this layer.
ARG GIT_SHA=local
ENV GIT_SHA=${GIT_SHA}

HEALTHCHECK --interval=15s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["sh", "-c", "exec dotnet \"$(cat /app/.entrypoint)\""]
