# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy shared projects first (for layer caching)
COPY src/Shared/ src/Shared/
COPY src/DeviceService/ src/DeviceService/
COPY proto/ proto/

RUN dotnet restore src/DeviceService/UavSystem.DeviceService.WebApi/UavSystem.DeviceService.WebApi.csproj

WORKDIR /src/src/DeviceService/UavSystem.DeviceService.WebApi
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8081 9081
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UavSystem.DeviceService.WebApi.dll"]
