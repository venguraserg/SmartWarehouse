# Сборка API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Сначала только файлы проектов — слой восстановления пакетов кешируется
COPY SmartWarehouse.Domain/SmartWarehouse.Domain.csproj SmartWarehouse.Domain/
COPY SmartWarehouse.Application/SmartWarehouse.Application.csproj SmartWarehouse.Application/
COPY SmartWarehouse.Infrastructure/SmartWarehouse.Infrastructure.csproj SmartWarehouse.Infrastructure/
COPY SmartWarehouse.API/SmartWarehouse.API.csproj SmartWarehouse.API/
RUN dotnet restore SmartWarehouse.API/SmartWarehouse.API.csproj

COPY . .
RUN dotnet publish SmartWarehouse.API/SmartWarehouse.API.csproj -c Release -o /app --no-restore

# Рабочий образ
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SmartWarehouse.API.dll"]
