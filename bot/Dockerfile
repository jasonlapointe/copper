# Build the single-service Telegram bot into a small runtime image for Cloud Run.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY CopperBot.csproj .
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
# Cloud Run sets PORT; the app reads it. Config comes from env / secrets at deploy time.
# Personas persist in Firestore (not local disk), so no data volume is needed.
EXPOSE 8080
ENTRYPOINT ["dotnet", "copper-bot.dll"]
