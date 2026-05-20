# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/Shared/ src/Shared/
COPY src/AlertService/ src/AlertService/
COPY proto/ proto/

RUN dotnet restore src/AlertService/UavSystem.AlertService.WebApi/UavSystem.AlertService.WebApi.csproj

WORKDIR /src/src/AlertService/UavSystem.AlertService.WebApi
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8084
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UavSystem.AlertService.WebApi.dll"]
