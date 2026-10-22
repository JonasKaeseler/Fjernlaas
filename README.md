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

Integrationstestene starter selv en SQL Server i Docker (Testcontainers), så Docker skal køre, når du kører `dotnet test`.

Appen svarer på `/health`. SQL Server kører på `localhost,14330`, og connection string til lokal udvikling ligger i `appsettings.Development.json`.

## Struktur

- `src/Fjernlaas.Web`: Blazor Server, API og (senere) Identity og EF Core
- `tests/Fjernlaas.Tests`: xUnit og integrationstests

## Platformsadmin

Platformsadmin oprettes ved opstart, hvis e-mail og adgangskode er sat. Lokalt med user-secrets:

```
dotnet user-secrets set "PlatformAdmin:Email" "din@mail.dk" --project src/Fjernlaas.Web
dotnet user-secrets set "PlatformAdmin:Password" "VælgEnKode1!" --project src/Fjernlaas.Web
```

Uden de to værdier sker der ingenting. En eksisterende bruger og dens adgangskode ændres aldrig.

## TTLock og SMS-webhook

Appens egne TTLock-oplysninger (ens for alle organisationer) sættes lokalt med user-secrets og i drift som miljøvariabler (`TTLock__ClientId`, `TTLock__ClientSecret`):

```
dotnet user-secrets set "TTLock:ClientId" "..." --project src/Fjernlaas.Web
dotnet user-secrets set "TTLock:ClientSecret" "..." --project src/Fjernlaas.Web
```

`TTLock:BaseUrl` er som standard den europæiske endpoint (`https://euapi.ttlock.com`).
Hver organisations egen TTLock-konto oprettes som en forbindelse i admin-UI'et. Adgangskoden gemmes kun som krypteret MD5-hash.

Webhook til InMobile: `POST` (eller `GET`) til `/api/sms/{token}`. Teksten er `afsendernummer;tekst`, fx `4512345678;Roshage`.
Token vises kun, når SMS-nummeret oprettes eller udskiftes. Test lokalt med Postman: `POST http://localhost:5080/api/sms/{token}` med teksten som body.

Kryptering bruger ASP.NET Core Data Protection. I drift skal nøglerne gemmes et sikkert sted (FND-6/FND-7), ellers kan gemte hemmeligheder ikke læses efter en ny udrulning.

## Drift

Opsætning af server, HTTPS, udrulning og sikkerhedskopi står i [docs/Drift.md](docs/Drift.md).
