# syntax=docker/dockerfile:1.7

# Build context is the repo root (see azure.yaml). Two-stage build keeps the
# final image to ASP.NET runtime + published output.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore using only csproj files to keep the layer cacheable across source
# changes.
COPY src/Quad5250.Protocol/Quad5250.Protocol.csproj src/Quad5250.Protocol/
COPY src/Quad5250.Server/Quad5250.Server.csproj src/Quad5250.Server/
RUN dotnet restore src/Quad5250.Server/Quad5250.Server.csproj

COPY src/Quad5250.Protocol/ src/Quad5250.Protocol/
COPY src/Quad5250.Server/ src/Quad5250.Server/
RUN dotnet publish src/Quad5250.Server/Quad5250.Server.csproj \
    -c Release \
    -o /app \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Quad5250.Server.dll"]
