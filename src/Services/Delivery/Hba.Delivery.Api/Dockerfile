# Contexte de build : la racine du dépôt (cf. DockerfileContext dans le csproj).
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

# Restauration en couche séparée : elle ne change que si les références changent.
COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY contracts/ ./contracts/
COPY src/BuildingBlocks/ ./src/BuildingBlocks/
COPY src/Services/Delivery/ ./src/Services/Delivery/

RUN apk add --no-cache protobuf grpc-plugins \
 && dotnet restore src/Services/Delivery/Hba.Delivery.Api/Hba.Delivery.Api.csproj

RUN dotnet publish src/Services/Delivery/Hba.Delivery.Api/Hba.Delivery.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

# Ne jamais tourner en root.
RUN adduser -D -u 10001 hba
USER hba

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080

COPY --from=build --chown=hba:hba /app/publish .

ENTRYPOINT ["dotnet", "Hba.Delivery.Api.dll"]
