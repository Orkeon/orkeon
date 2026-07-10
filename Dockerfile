# Stage 1: Build
# GA image + global.json: the 10.0-preview SDK band ships a Roslyn that breaks the
# Orkeon.Generators source generator (CS8795 partial methods without implementation).
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files first (layer caching)
COPY Orkeon.sln .
COPY global.json .
COPY Directory.Packages.props .
COPY src/Directory.Build.props src/
COPY src/core/Orkeon.Domain/Orkeon.Domain.csproj src/core/Orkeon.Domain/
COPY src/core/Orkeon.Application/Orkeon.Application.csproj src/core/Orkeon.Application/
COPY src/core/Orkeon.Infrastructure/Orkeon.Infrastructure.csproj src/core/Orkeon.Infrastructure/
COPY src/tools/Orkeon.Tools.Abstractions/Orkeon.Tools.Abstractions.csproj src/tools/Orkeon.Tools.Abstractions/
COPY src/tools/Orkeon.Tools.Code/Orkeon.Tools.Code.csproj src/tools/Orkeon.Tools.Code/
COPY src/tools/Orkeon.Tools.Data/Orkeon.Tools.Data.csproj src/tools/Orkeon.Tools.Data/
COPY src/tools/Orkeon.Tools.FileSystem/Orkeon.Tools.FileSystem.csproj src/tools/Orkeon.Tools.FileSystem/
COPY src/tools/Orkeon.Tools.Web/Orkeon.Tools.Web.csproj src/tools/Orkeon.Tools.Web/
COPY src/plugins/Orkeon.Plugins/Orkeon.Plugins.csproj src/plugins/Orkeon.Plugins/
COPY src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj src/apps/Orkeon.ConsoleApp/

# Restore
RUN dotnet restore src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj

# Copy source files and build
COPY src/ src/
RUN dotnet publish src/apps/Orkeon.ConsoleApp/Orkeon.ConsoleApp.csproj -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .

# Run as non-root user
RUN adduser --disabled-password --gecos "" app
USER app

ENTRYPOINT ["dotnet", "Orkeon.ConsoleApp.dll"]
