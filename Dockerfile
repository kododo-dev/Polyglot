# The pages' Way.Shell module: plain JavaScript, so it is built once on the builder's platform.
FROM --platform=$BUILDPLATFORM node:22-alpine AS ui
WORKDIR /ui
COPY src/Polyglot.Web/UI/package.json src/Polyglot.Web/UI/package-lock.json ./
RUN npm ci
COPY src/Polyglot.Web/UI/ ./
RUN npm run build

# The SDK stage runs on the builder's platform and cross-compiles for the target one,
# so the arm64 image is built without emulation.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG VERSION=0.0.0-dev
WORKDIR /src
COPY src/Directory.Build.props src/Directory.Packages.props ./
COPY src/Polyglot.Web/Polyglot.Web.csproj ./Polyglot.Web/
RUN dotnet restore Polyglot.Web/Polyglot.Web.csproj -a $TARGETARCH
COPY src/Polyglot.Web/ ./Polyglot.Web/
COPY --from=ui /ui/dist ./Polyglot.Web/UI/dist/
RUN dotnet publish Polyglot.Web/Polyglot.Web.csproj \
    --configuration Release \
    --no-restore \
    -a $TARGETARCH \
    -p:Version=$VERSION \
    --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app ./
# The base image already listens on 8080 (HTTP_PORTS); behind a proxy trust X-Forwarded-* headers.
ENV ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "Kododo.Polyglot.Web.dll"]
