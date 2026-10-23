# Bygger Fjernlås til drift. Billedet indeholder ingen hemmeligheder; alt hentes fra miljøvariabler ved start.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Fjernlaas.slnx ./
COPY src/ src/
# Restore og publish i ét trin, ligesom en lokal "dotnet publish". Et separat restore med kun projektfilen gav et billede uden
# Blazors scripts (404 på blazor.web.js), så det er fjernet.
RUN dotnet publish src/Fjernlaas.Web/Fjernlaas.Web.csproj -c Release -o /app
# Bygget fejler hellere end at udgive et billede, hvor admin-siderne ikke kan blive interaktive.
RUN test -f /app/wwwroot/_framework/blazor.web.js || (echo "blazor.web.js mangler i publish-mappen" && ls -R /app/wwwroot && exit 1)

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
