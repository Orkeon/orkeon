# Stage 1: Build
# GA image + global.json: the 10.0-preview SDK band ships a Roslyn that breaks the
# Orkeon.Generators source generator (CS8795 partial methods without implementation).
FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:2fa828c68761b1b8c23d7662dc134421b9d3b59fe1425fdbc80804e390cdb24d AS build
WORKDIR /src

# The whole source tree in one layer, restore against it, publish.
#
# This used to hand-write the project graph as a list of per-csproj COPY lines, so the
# restore layer would only be invalidated when a .csproj changed. The list drifted — it
# named 10 of the 22 projects Orkeon.ConsoleApp actually references, and carried one it no
# longer does — so `docker build .` failed at the restore step and nothing in CI built this
# file to notice. A Dockerfile that does not build is worth less than a slower one: the
# graph is now read from the tree instead of restated beside it.
# Directory.Build.props is not optional: src/Directory.Build.props imports the file above it
# with an UNCONDITIONAL <Import>, so when the root one is missing the expression evaluates to
# "" and MSBuild refuses it (MSB4020). `dotnet restore` tolerates the empty import; `publish`
# does not — which is why omitting it produced a green restore layer and a failing publish.
COPY Orkeon.sln global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/

RUN dotnet restore src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj
RUN dotnet publish src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0@sha256:b89586dc17781f25531909993658aa8161205ae38b8cec8847df4a8221a403d5 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# The image redistributes the publish's whole package closure and the model weights it
# embeds: their notices go with it, at the Debian package's path. The .NET runtime is the
# base image's, which carries its own.
COPY LICENSE.md THIRD-PARTY-NOTICES.md /usr/share/doc/orkeon/

# Run as the non-root `app` user built into the GA runtime image
# (the minimal runtime:10.0 image has no `adduser`).
USER app

ENTRYPOINT ["dotnet", "Orkeon.ConsoleApp.dll"]
