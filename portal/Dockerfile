# Stage 1: Build Angular admin app (DEMO SCAFFOLDING: simulator UI included)
FROM node:22 AS web-build
WORKDIR /src/web
COPY web/package*.json ./
RUN npm ci
COPY web/ ./
RUN node node_modules/@angular/cli/bin/ng.js build admin-portal --configuration production

# Stage 2: Publish .NET API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY api/ ./api/
RUN dotnet publish api/src/Mod.PortalApi/Mod.PortalApi.csproj \
    --configuration Release \
    --output /publish

# Stage 3: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

RUN useradd --no-create-home --shell /bin/false --uid 10001 appuser

COPY --from=api-build /publish ./
COPY --from=web-build /src/web/dist/admin-portal/browser ./wwwroot/

USER appuser
ENTRYPOINT ["dotnet", "Mod.PortalApi.dll"]
