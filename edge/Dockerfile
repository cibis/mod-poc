FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Mod.Collector/Mod.Collector.csproj src/Mod.Collector/
RUN dotnet restore src/Mod.Collector/Mod.Collector.csproj
COPY src/ src/
RUN dotnet publish src/Mod.Collector/Mod.Collector.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN useradd -r -u 1001 appuser
COPY --from=build /app/publish .
USER appuser
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mod.Collector.dll"]
