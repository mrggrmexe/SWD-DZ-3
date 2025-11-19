# --- Stage 1: Build ---
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Копируем csproj, чтобы кэшировались restore-слои
COPY Gateway/Gateway.csproj Gateway/
COPY Shared/Shared.csproj Shared/

# Восстанавливаем зависимости
RUN dotnet restore Gateway/Gateway.csproj

# Копируем всё решение
COPY . .

# Публикуем
RUN dotnet publish Gateway/Gateway.csproj -c Release -o /app/publish

# --- Stage 2: Runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

# Kestrel будет слушать на 8080 внутри контейнера
EXPOSE 8080

ENTRYPOINT ["dotnet", "Gateway.dll"]
