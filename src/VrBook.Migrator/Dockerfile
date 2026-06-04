FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY src/ src/
RUN dotnet publish src/VrBook.Migrator/VrBook.Migrator.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
RUN groupadd --system vrbook && useradd --system --gid vrbook vrbook
USER vrbook
COPY --from=build --chown=vrbook:vrbook /app/publish .
# TEMP DEBUG: print what's in /app + try dotnet --info + try to run the app explicitly
# capturing both stdout+stderr through `2>&1`. Revert once we see real output.
ENTRYPOINT ["sh", "-c", "echo '=== ls /app ==='; ls -la /app; echo '=== runtimeconfig ==='; cat /app/VrBook.Migrator.runtimeconfig.json || true; echo '=== dotnet --info ==='; dotnet --info; echo '=== running app ==='; dotnet /app/VrBook.Migrator.dll 2>&1; echo '=== exit: '$?"]
