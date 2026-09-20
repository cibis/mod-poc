# Stage 1: Build Angular customer-portal app
FROM node:22 AS web-build
WORKDIR /src/web
COPY web/package.json web/package-lock.json ./
RUN npm ci
COPY web/ ./
RUN node node_modules/@angular/cli/bin/ng.js build customer-portal --configuration production

# Stage 2: Publish .NET reporting API
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS api-build
WORKDIR /src
COPY api/ ./api/
RUN dotnet publish api/src/Mod.ReportingApi/Mod.ReportingApi.csproj \
    -c Release -o /app/publish --no-self-contained

# Stage 3: Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
RUN useradd --no-create-home --shell /bin/false --uid 10000 appuser
COPY --from=api-build /app/publish ./
COPY --from=web-build /src/web/dist/customer-portal/browser ./wwwroot/
USER appuser
EXPOSE 8080
ENTRYPOINT ["dotnet", "Mod.ReportingApi.dll"]
