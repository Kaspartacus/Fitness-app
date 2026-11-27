FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY global.json ./
COPY src/ ./src/
RUN dotnet publish src/FitnessApp.Server/FitnessApp.Server.csproj -c Release -o /publish \
    --disable-build-servers -m:1 -p:BuildInParallel=false -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /publish ./
USER $APP_UID
ENTRYPOINT ["dotnet", "FitnessApp.Server.dll"]
