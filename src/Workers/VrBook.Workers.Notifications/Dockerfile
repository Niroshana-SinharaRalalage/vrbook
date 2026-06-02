FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY src/ src/
RUN dotnet publish src/Workers/VrBook.Workers.Notifications/VrBook.Workers.Notifications.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS runtime
WORKDIR /app
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
RUN addgroup -S vrbook && adduser -S vrbook -G vrbook
USER vrbook
COPY --from=build --chown=vrbook:vrbook /app/publish .
ENTRYPOINT ["dotnet", "VrBook.Workers.Notifications.dll"]
