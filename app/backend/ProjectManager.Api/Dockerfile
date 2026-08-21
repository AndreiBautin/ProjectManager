# Multi-stage: the SDK is needed to build, but shipping it would mean a runtime
# image an order of magnitude larger than the app inside it.
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# The project file alone first, so `restore` gets its own cache layer and only
# re-runs when the dependency list changes - not on every source edit.
COPY ProjectManager.Api.csproj ./
RUN dotnet restore ProjectManager.Api.csproj

COPY . ./
RUN dotnet publish ProjectManager.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime

COPY --from=build /app/publish /app

# SQLite writes its database file relative to the process working directory, and
# /app is owned by root. A separate writable directory keeps the container able
# to run as a non-root user - the base image provides `app` for exactly this.
RUN mkdir -p /data && chown $APP_UID:$APP_UID /data
WORKDIR /data
USER $APP_UID

ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# $PORT has to be read at RUN time, not build time: container hosts assign it
# per-deploy. An `ENV ASPNETCORE_URLS=...${PORT}` line would be resolved when the
# image is built, when PORT does not exist yet, and would silently bind the
# wrong port. `exec` keeps dotnet as PID 1 so it still receives SIGTERM.
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet /app/ProjectManager.Api.dll --urls http://+:${PORT:-8080}"]
