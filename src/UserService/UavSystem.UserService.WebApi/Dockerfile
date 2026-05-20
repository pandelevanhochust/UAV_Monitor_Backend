# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy shared projects first (for layer caching)
COPY src/Shared/ src/Shared/
COPY src/UserService/ src/UserService/
COPY proto/ proto/

RUN dotnet restore src/UserService/UavSystem.UserService.WebApi/UavSystem.UserService.WebApi.csproj

WORKDIR /src/src/UserService/UavSystem.UserService.WebApi
RUN dotnet publish -c Release -o /app/publish --no-restore

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080 9080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "UavSystem.UserService.WebApi.dll"]
