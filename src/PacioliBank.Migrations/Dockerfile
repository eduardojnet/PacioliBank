# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props ./
COPY src/PacioliBank.Migrations/PacioliBank.Migrations.csproj src/PacioliBank.Migrations/
RUN dotnet restore src/PacioliBank.Migrations/PacioliBank.Migrations.csproj

COPY db/ db/
COPY src/PacioliBank.Migrations/ src/PacioliBank.Migrations/
RUN dotnet publish src/PacioliBank.Migrations/PacioliBank.Migrations.csproj -c Release -o /app --no-restore

# Imagem de runtime sem ASP.NET: o migrador e um console que aplica e termina.
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

USER $APP_UID

ENTRYPOINT ["dotnet", "PacioliBank.Migrations.dll"]
