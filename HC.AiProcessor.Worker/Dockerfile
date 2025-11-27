#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

ARG token
ARG nuget_user

ENV DOTNET_NUGET_SIGNATURE_VERIFICATION=false

#ARG ACCESS_TOKEN
#ARG ARTIFACTS_ENDPOINT
#ARG USER_NAME
#
#RUN wget -qO- https://raw.githubusercontent.com/Microsoft/artifacts-credprovider/master/helpers/installcredprovider.sh | bash
#ENV DOTNET_SYSTEM_NET_HTTP_USESOCKETSHTTPHANDLER=0
#ENV NUGET_CREDENTIALPROVIDER_SESSIONTOKENCACHE_ENABLED true
#ENV VSS_NUGET_EXTERNAL_FEED_ENDPOINTS {\"endpointCredentials\": [{\"endpoint\":\"${ARTIFACTS_ENDPOINT}\", \"username\":\"${USER_NAME}\", \"password\":\"${ACCESS_TOKEN}\"}]}

RUN apt-get update && apt-get install -y curl

RUN dotnet nuget add source --username ${nuget_user} --password ${token} --store-password-in-clear-text --name github "https://nuget.pkg.github.com/adeo/index.json"
RUN dotnet nuget add source --username ${nuget_user} --password ${token} --store-password-in-clear-text --name github2 "https://git.ithoot.com/api/v4/projects/36/packages/nuget/index.json"

COPY ["./HC.AiProcessor.Worker/HC.AiProcessor.Worker.csproj", "./HC.AiProcessor.Worker/HC.AiProcessor.Worker.csproj"]
RUN dotnet restore "./HC.AiProcessor.Worker/HC.AiProcessor.Worker.csproj"

COPY . .
WORKDIR "/src/HC.AiProcessor.Worker"
RUN dotnet build "HC.AiProcessor.Worker.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HC.AiProcessor.Worker.csproj" -c Release -o /app/publish

RUN dotnet dev-certs https

FROM base AS final
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ARG SECRETS=/opt/vault_secrets/credentials
ENV SECRETS=${SECRETS}
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["/bin/bash","-c", "[ -f $SECRETS ] && source $SECRETS; dotnet HC.AiProcessor.Worker.dll"]
