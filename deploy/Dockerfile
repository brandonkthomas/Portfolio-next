# syntax=docker/dockerfile:1.7

ARG NODE_VERSION=24.19.0
ARG DOTNET_SDK_VERSION=10.0.400
ARG ASPNET_VERSION=10.0.11

# Type-check, bundle, and minify first-party scripts and the stylesheet; this stage is excluded from runtime
FROM node:${NODE_VERSION}-alpine3.23 AS client-build
WORKDIR /source

COPY package.json package-lock.json tsconfig.json ./
RUN npm ci --ignore-scripts

COPY src/Portfolio.Web/Client ./src/Portfolio.Web/Client
COPY src/Portfolio.Web/Styles ./src/Portfolio.Web/Styles
COPY tools/build-client.mjs ./tools/build-client.mjs
RUN npm run build:client:production

# Restore and publish the Razor Pages application; the locked restore keeps builds reproducible
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_VERSION}-alpine3.23 AS dotnet-build
WORKDIR /source

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/Portfolio.Web/Portfolio.Web.csproj src/Portfolio.Web/packages.lock.json ./src/Portfolio.Web/
RUN dotnet restore src/Portfolio.Web/Portfolio.Web.csproj --locked-mode

COPY src/Portfolio.Web ./src/Portfolio.Web
COPY --from=client-build /source/src/Portfolio.Web/wwwroot/assets/js ./src/Portfolio.Web/wwwroot/assets/js
COPY --from=client-build /source/src/Portfolio.Web/wwwroot/css ./src/Portfolio.Web/wwwroot/css
RUN dotnet publish src/Portfolio.Web/Portfolio.Web.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    --property:UseAppHost=false \
    && test -s /app/publish/Content/projects.v1.json

# Runtime contains only published app output and runs as the unprivileged image user
FROM mcr.microsoft.com/dotnet/aspnet:${ASPNET_VERSION}-alpine3.23 AS runtime
WORKDIR /app

ARG PORTFOLIO_RELEASE_ID=unversioned
ARG PORTFOLIO_SOURCE_REVISION=unknown

LABEL org.opencontainers.image.revision="${PORTFOLIO_SOURCE_REVISION}" \
      net.brandonthomas.portfolio.release="${PORTFOLIO_RELEASE_ID}"

ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0

EXPOSE 8080

COPY --from=dotnet-build --chown=$APP_UID:$APP_UID /app/publish ./

USER $APP_UID

HEALTHCHECK --interval=5s --timeout=2s --start-period=5s --retries=10 \
    CMD wget --spider --quiet http://127.0.0.1:8080/health/ready || exit 1

ENTRYPOINT ["dotnet", "Portfolio.Web.dll"]
