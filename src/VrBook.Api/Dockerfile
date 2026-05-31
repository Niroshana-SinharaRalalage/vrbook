# =====================================================================================
# VrBook.Api — multi-stage build for Container Apps.
# =====================================================================================
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src

# Copy *.csproj first so layer caching works across source-only changes.
COPY src/Directory.Build.props src/Directory.Build.targets src/Directory.Packages.props src/global.json src/
COPY src/VrBook.Contracts/VrBook.Contracts.csproj            src/VrBook.Contracts/
COPY src/VrBook.Domain/VrBook.Domain.csproj                  src/VrBook.Domain/
COPY src/VrBook.Application/VrBook.Application.csproj        src/VrBook.Application/
COPY src/VrBook.Infrastructure/VrBook.Infrastructure.csproj  src/VrBook.Infrastructure/
COPY src/VrBook.Api/VrBook.Api.csproj                        src/VrBook.Api/
COPY src/Modules/                                            src/Modules/
# (The Modules/*.csproj files are pulled in via the glob above)

RUN dotnet restore src/VrBook.Api/VrBook.Api.csproj

# Copy the rest of the source.
COPY src/ src/
COPY contracts/openapi.yaml contracts/openapi.yaml

RUN dotnet publish src/VrBook.Api/VrBook.Api.csproj \
    -c Release -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ----- Runtime -----
FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app

# Run as non-root.
RUN addgroup -S vrbook && adduser -S vrbook -G vrbook
USER vrbook

COPY --from=build --chown=vrbook:vrbook /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --retries=3 \
  CMD wget -q --spider http://127.0.0.1:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "VrBook.Api.dll"]
