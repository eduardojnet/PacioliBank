# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props ./
COPY src/PacioliBank.Ledger/PacioliBank.Ledger.csproj src/PacioliBank.Ledger/
COPY src/PacioliBank.Ledger.Persistence/PacioliBank.Ledger.Persistence.csproj src/PacioliBank.Ledger.Persistence/
COPY src/PacioliBank.Api/PacioliBank.Api.csproj src/PacioliBank.Api/
RUN dotnet restore src/PacioliBank.Api/PacioliBank.Api.csproj

COPY src/ src/
RUN dotnet publish src/PacioliBank.Api/PacioliBank.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./

# RNF-023 e boa pratica de container: processo sem privilegio de root.
USER $APP_UID

EXPOSE 8080
ENTRYPOINT ["dotnet", "PacioliBank.Api.dll"]
