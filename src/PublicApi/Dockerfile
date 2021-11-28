#See https://aka.ms/containerfastmode to understand how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:5.0-buster-slim AS base
WORKDIR /app

EXPOSE 80

# copy only csproj files in prod case
FROM mcr.microsoft.com/dotnet/sdk:5.0-buster-slim AS build
WORKDIR /app
COPY . .

WORKDIR "/app/src/PublicApi"
RUN dotnet restore

RUN dotnet build "./PublicApi.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "./PublicApi.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

## Optional: Set this here if not setting it from docker-compose.yml
#ENV ASPNETCORE_ENVIRONMENT Docker # Development Docker
ENV ASPNETCORE_URLS http://+:80

ENTRYPOINT ["dotnet", "PublicApi.dll"]
