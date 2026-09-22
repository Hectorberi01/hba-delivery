# Contexte de build : la racine du depot.
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY contracts/ ./contracts/
COPY src/BuildingBlocks/ ./src/BuildingBlocks/
COPY src/Services/Driver/ ./src/Services/Driver/

RUN apk add --no-cache protobuf grpc-plugins \
 && dotnet restore src/Services/Driver/Hba.Driver.Api/Hba.Driver.Api.csproj

RUN dotnet publish src/Services/Driver/Hba.Driver.Api/Hba.Driver.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

RUN adduser -D -u 10001 hba
USER hba

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080

COPY --from=build --chown=hba:hba /app/publish .

ENTRYPOINT ["dotnet", "Hba.Driver.Api.dll"]
