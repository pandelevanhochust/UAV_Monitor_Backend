# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Shared/ src/Shared/
COPY src/LogService/ src/LogService/
COPY proto/ proto/

RUN dotnet restore src/LogService/UavSystem.LogService.WebApi/UavSystem.LogService.WebApi.csproj

WORKDIR /src/src/LogService/UavSystem.LogService.WebApi
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8083
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UavSystem.LogService.WebApi.dll"]
