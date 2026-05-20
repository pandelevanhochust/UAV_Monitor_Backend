# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Shared/ src/Shared/
COPY src/IngestionService/ src/IngestionService/
COPY proto/ proto/

RUN dotnet restore src/IngestionService/UavSystem.IngestionService.WebApi/UavSystem.IngestionService.WebApi.csproj

WORKDIR /src/src/IngestionService/UavSystem.IngestionService.WebApi
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8082
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UavSystem.IngestionService.WebApi.dll"]
