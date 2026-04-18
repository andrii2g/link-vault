FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY LinkVault.slnx ./
COPY src/LinkVault.Api/LinkVault.Api.csproj src/LinkVault.Api/
RUN dotnet restore src/LinkVault.Api/LinkVault.Api.csproj

COPY src/LinkVault.Api/ src/LinkVault.Api/
RUN dotnet publish src/LinkVault.Api/LinkVault.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://0.0.0.0:5678
ENV LinkVault__DataPath=/data/links.json

VOLUME ["/data"]
EXPOSE 5678

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "LinkVault.Api.dll"]
