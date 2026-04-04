# Stage 1: Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy project files and restore
COPY Migrify.Core/Migrify.Core.csproj Migrify.Core/
COPY Migrify.Infrastructure/Migrify.Infrastructure.csproj Migrify.Infrastructure/
COPY Migrify.Web/Migrify.Web.csproj Migrify.Web/
RUN dotnet restore Migrify.Web/Migrify.Web.csproj

# Copy everything and build
COPY . .
RUN dotnet publish Migrify.Web/Migrify.Web.csproj -c Release -o /app/publish -p:StaticWebAssetsEnabled=true

# Stage 2: Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Install curl for health checks
RUN apt-get update && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

# Create non-root user
RUN groupadd -r migrify && useradd -r -g migrify -d /app -s /sbin/nologin migrify

# Create directories for logs and data protection keys
RUN mkdir -p /app/logs /app/keys && chown -R migrify:migrify /app

COPY --from=build /app/publish .
RUN chown -R migrify:migrify /app

USER migrify

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Migrify.Web.dll"]
