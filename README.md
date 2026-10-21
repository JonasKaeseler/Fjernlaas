# Fjernlås

Beta-platform til SMS-styret adgang. Se backloggen i Claude Docs for opgaver.

## Forudsætninger

- .NET 10 SDK (versionen er låst i `global.json`)
- Docker Desktop

## Kom i gang

```
docker compose up -d
dotnet build
dotnet test
dotnet run --project src/Fjernlaas.Web
```

Appen svarer på `/health`. SQL Server kører på `localhost,14330`, og connection string til lokal udvikling ligger i `appsettings.Development.json`.

## Struktur

- `src/Fjernlaas.Web`: Blazor Server, API og (senere) Identity og EF Core
- `tests/Fjernlaas.Tests`: xUnit og integrationstests
