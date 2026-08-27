# Stage 1: Build
# GA image + global.json: the 10.0-preview SDK band ships a Roslyn that breaks the
# Orkeon.Generators source generator (CS8795 partial methods without implementation).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# The whole source tree in one layer, restore against it, publish.
#
# This used to hand-write the project graph as a list of per-csproj COPY lines, so the
# restore layer would only be invalidated when a .csproj changed. The list drifted — it
# named 10 of the 22 projects Orkeon.ConsoleApp actually references, and carried one it no
# longer does — so `docker build .` failed at the restore step and nothing in CI built this
# file to notice. A Dockerfile that does not build is worth less than a slower one: the
# graph is now read from the tree instead of restated beside it.
COPY Orkeon.sln global.json Directory.Packages.props ./
COPY src/ src/

RUN dotnet restore src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj
RUN dotnet publish src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Run as the non-root `app` user built into the GA runtime image
# (the minimal runtime:10.0 image has no `adduser`).
USER app

ENTRYPOINT ["dotnet", "Orkeon.ConsoleApp.dll"]
