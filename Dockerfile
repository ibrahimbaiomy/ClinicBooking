# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Shared build rules first, so the container build uses the same settings as local.
COPY Directory.Build.props .editorconfig ./

# Project files only, for a cacheable restore layer. The tests project is not
# part of the image, so restore targets the Api project rather than the solution.
COPY src/ClinicBooking.Domain/ClinicBooking.Domain.csproj src/ClinicBooking.Domain/
COPY src/ClinicBooking.Application/ClinicBooking.Application.csproj src/ClinicBooking.Application/
COPY src/ClinicBooking.Infrastructure/ClinicBooking.Infrastructure.csproj src/ClinicBooking.Infrastructure/
COPY src/ClinicBooking.Api/ClinicBooking.Api.csproj src/ClinicBooking.Api/
RUN dotnet restore src/ClinicBooking.Api/ClinicBooking.Api.csproj

COPY src/ src/
RUN dotnet publish src/ClinicBooking.Api/ClinicBooking.Api.csproj \
    --configuration Release --no-restore --output /app/publish

# Standard Debian-based runtime image (not Alpine/chiseled): it ships tzdata (D12).
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

# Built-in non-root user of the aspnet image.
USER $APP_UID

ENTRYPOINT ["dotnet", "ClinicBooking.Api.dll"]