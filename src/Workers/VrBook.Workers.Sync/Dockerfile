FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY src/ src/
RUN dotnet publish src/Workers/VrBook.Workers.Sync/VrBook.Workers.Sync.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS runtime
WORKDIR /app
RUN addgroup -S vrbook && adduser -S vrbook -G vrbook
USER vrbook
COPY --from=build --chown=vrbook:vrbook /app/publish .
ENTRYPOINT ["dotnet", "VrBook.Workers.Sync.dll"]
