# Contexte de build : la racine du depot.
FROM mcr.microsoft.com/dotnet/sdk:9.0-alpine AS build
WORKDIR /src

COPY global.json Directory.Build.props Directory.Build.targets Directory.Packages.props ./
COPY contracts/ ./contracts/
COPY src/BuildingBlocks/ ./src/BuildingBlocks/
COPY src/Services/Payment/ ./src/Services/Payment/

# Grpc.Tools embarque un protoc lie a la glibc. Sur Alpine (musl), l'exec
# echoue en ENOENT — « No such file or directory » alors que le fichier est bien
# la : c'est son chargeur dynamique qui manque. On installe donc les binaires
# musl natifs, et surtout on DIT a MSBuild de les utiliser : sans ces deux
# proprietes, Grpc.Tools reprend son binaire embarque et l'erreur revient.
ENV Protobuf_ProtocFullPath=/usr/bin/protoc \
    gRPC_PluginFullPath=/usr/bin/grpc_csharp_plugin

RUN apk add --no-cache protobuf grpc-plugins \
 && test -x "$Protobuf_ProtocFullPath" \
 && test -x "$gRPC_PluginFullPath" \
 && dotnet restore src/Services/Payment/Hba.Payment.Api/Hba.Payment.Api.csproj

RUN dotnet publish src/Services/Payment/Hba.Payment.Api/Hba.Payment.Api.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ETAGE D'EXECUTION SUR DEBIAN, PAS ALPINE, ET C'EST DELIBERE.
# Confluent.Kafka embarque librdkafka liee a la glibc. Sur Alpine (musl), le
# chargement echoue en DllNotFoundException. Le RID musl-arm64 n'est pas fourni
# par le paquet, et la librdkafka d'Alpine derive en version de celle qu'attend
# le client : on aurait le meme plantage, plus tard et moins lisible.
# L'etage de build, lui, reste sur Alpine : sa sortie est portable.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN useradd --uid 10001 --create-home --shell /usr/sbin/nologin hba
USER hba

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_gcServer=1
EXPOSE 8080

COPY --from=build --chown=hba:hba /app/publish .

ENTRYPOINT ["dotnet", "Hba.Payment.Api.dll"]
