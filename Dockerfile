# Bygger Fjernlås til drift. Billedet indeholder ingen hemmeligheder; alt hentes fra miljøvariabler ved start.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Fjernlaas.slnx ./
COPY src/Fjernlaas.Web/Fjernlaas.Web.csproj src/Fjernlaas.Web/
RUN dotnet restore src/Fjernlaas.Web/Fjernlaas.Web.csproj
COPY src/ src/
RUN dotnet publish src/Fjernlaas.Web/Fjernlaas.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DataProtection__KeyPath=/keys
# Mappen til Data Protection-nøgler. Skal være en volumen, ellers mistes nøglerne ved ny udgivelse.
RUN mkdir -p /keys && chown -R $APP_UID /keys
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Fjernlaas.Web.dll"]
