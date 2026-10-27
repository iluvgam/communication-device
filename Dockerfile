FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Relay/Relay.csproj Relay/
COPY Shared/ Shared/
RUN dotnet restore Relay/Relay.csproj
COPY Relay/ Relay/
RUN dotnet publish Relay/Relay.csproj -c Release --no-restore -o /out /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
ENV ASPNETCORE_URLS=http://0.0.0.0:10000
EXPOSE 10000
USER app
ENTRYPOINT ["dotnet", "Relay.dll"]
