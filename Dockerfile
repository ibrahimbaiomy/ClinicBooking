# syntax=docker/dockerfile:1

# Stage 1: the Angular bundle (D15). Package files first for a cacheable `npm ci`; the lock file is
# committed, so the install is exact. The whole project is copied next because `npm run build`
# runs the prebuild checks, which need scripts/, public/i18n and src/.
FROM node:24-bookworm-slim AS web
WORKDIR /web
COPY src/clinic-booking-web/package.json src/clinic-booking-web/package-lock.json src/clinic-booking-web/.npmrc ./
RUN npm ci
COPY src/clinic-booking-web/ ./
RUN npm run build

# Stage 2: the API.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Shared build rules first, so the container build uses the same settings as local.
COPY global.json Directory.Build.props .editorconfig ./

# Project files only, for a cacheable restore layer. The tests project is not
# part of the image, so restore targets the Api project rather than the solution.
COPY src/ClinicBooking.Domain/ClinicBooking.Domain.csproj src/ClinicBooking.Domain/
COPY src/ClinicBooking.Application/ClinicBooking.Application.csproj src/ClinicBooking.Application/
COPY src/ClinicBooking.Infrastructure/ClinicBooking.Infrastructure.csproj src/ClinicBooking.Infrastructure/
COPY src/ClinicBooking.Api/ClinicBooking.Api.csproj src/ClinicBooking.Api/
RUN dotnet restore src/ClinicBooking.Api/ClinicBooking.Api.csproj

# Only the .NET projects: a front-end change must not invalidate this layer.
COPY src/ClinicBooking.Domain/ src/ClinicBooking.Domain/
COPY src/ClinicBooking.Application/ src/ClinicBooking.Application/
COPY src/ClinicBooking.Infrastructure/ src/ClinicBooking.Infrastructure/
COPY src/ClinicBooking.Api/ src/ClinicBooking.Api/
RUN dotnet publish src/ClinicBooking.Api/ClinicBooking.Api.csproj \
    --configuration Release --no-restore --output /app/publish

# Standard Debian-based runtime image (not Alpine/chiseled): it ships tzdata (D12).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
# The built front end becomes the web root; the API serves it with a SPA fallback.
COPY --from=web /web/dist/clinic-booking-web/browser ./wwwroot

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Built-in non-root user of the aspnet image.
USER $APP_UID

ENTRYPOINT ["dotnet", "ClinicBooking.Api.dll"]
