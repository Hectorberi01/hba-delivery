FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY contracts/ ./contracts/
COPY src/BuildingBlocks/ ./src/BuildingBlocks/
COPY src/Gateways/Hba.Bff.Driver/ ./src/Gateways/Hba.Bff.Driver/

RUN apk add --no-cache protobuf grpc-plugins \
 && dotnet restore src/Gateways/Hba.Bff.Driver/Hba.Bff.Driver.csproj

RUN dotnet publish src/Gateways/Hba.Bff.Driver/Hba.Bff.Driver.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0-alpine AS runtime
WORKDIR /app

RUN adduser -D -u 10001 hba
USER hba

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

COPY --from=build --chown=hba:hba /app/publish .

ENTRYPOINT ["dotnet", "Hba.Bff.Driver.dll"]
