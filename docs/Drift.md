# Drift af Fjernlås

Appen kører som tre containere på en almindelig server med Docker: appen, SQL Server og Caddy (HTTPS). Det kræver en server med mindst 2 GB RAM (SQL Server bruger omkring 1,5 GB), et domænenavn og åbne porte 80 og 443.

## Første gang

1. **Server.** Opret en Linux-server (fx Ubuntu 24.04) hos en udbyder, og installér Docker (`curl -fsSL https://get.docker.com | sh`). Åbn kun porte 22, 80 og 443 i firewallen. Slå login med adgangskode over SSH fra.
2. **Domæne.** Peg et A-record (fx `fjernlaas.jeresdomaene.dk`) på serverens IP.
3. **Filer.** Opret mappen `~/fjernlaas` på serveren og kopiér indholdet af `deploy/` dertil (`docker-compose.prod.yml` og `Caddyfile`). Kopiér `.env.prod.example` til `.env` og udfyld værdierne. `.env` må kun findes på serveren.
4. **Billede.** Kør workflowet "Udgiv" i GitHub (Actions, Run workflow). Det lægger billedet i GitHub Container Registry. Gør pakken privat, og opret en token med kun læserettigheden `read:packages`. Log ind på serveren med den: `echo <token> | docker login ghcr.io -u <brugernavn> --password-stdin`.
5. **Start.** `cd ~/fjernlaas && docker compose -f docker-compose.prod.yml up -d`. Databasen oprettes og migreres ved første start.
6. **Platformsadmin.** Er `PLATFORMADMIN_EMAIL` og `PLATFORMADMIN_PASSWORD` sat i `.env`, oprettes kontoen ved opstart. Log ind, skift adgangskoden, og fjern adgangskoden fra `.env`.
7. **InMobile.** Opret SMS-nummeret i admin, og peg InMobile på adressen `https://<domæne>/api/sms/<token>`.

## Automatisk udrulning (valgfrit)

Sæt disse secrets i GitHub (Settings, Secrets and variables, Actions), så udrulles hver kørsel af "Udgiv" til serveren: `DEPLOY_HOST`, `DEPLOY_USER`, `DEPLOY_SSH_KEY` (en privat nøgle kun til dette) og `GHCR_READ_TOKEN`. Uden dem bygges billedet kun.

## Sikkerhedskopi

Det er databasen og Data Protection-nøglerne, der skal gemmes. Nøglerne (volumen `dp-keys`) krypterer TTLock-adgangen i databasen. Mister du dem, skal forbindelserne oprettes igen. Tag en kopi af begge samtidig, og gem den uden for serveren:

```
docker compose -f docker-compose.prod.yml exec sqlserver /opt/mssql-tools18/bin/sqlcmd -C -S localhost -U sa -P "$SA_PASSWORD" -Q "BACKUP DATABASE Fjernlaas TO DISK='/var/opt/mssql/data/Fjernlaas.bak' WITH INIT"
```

Brug serverudbyderens ugentlige snapshot som ekstra sikkerhed.

## Tjek

- `https://<domæne>/health` svarer `{"status":"ok"}`.
- Log ind og tryk "Vis låse" under TTLock.
- Send en POST til webhook-adressen, og se resultatet under Oplåsningslog.
