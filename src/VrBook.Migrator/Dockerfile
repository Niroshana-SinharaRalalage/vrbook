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
ENTRYPOINT ["dotnet", "VrBook.Migrator.dll"]
