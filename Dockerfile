# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy solution and project files
COPY SocietyLedger.sln ./
COPY SocietyLedger.Api/SocietyLedger.Api.csproj src/SocietyLedger.Api/
COPY SocietyLedger.Application/SocietyLedger.Application.csproj src/SocietyLedger.Application/
COPY SocietyLedger.Domain/SocietyLedger.Domain.csproj src/SocietyLedger.Domain/
COPY SocietyLedger.Infrastructure/SocietyLedger.Infrastructure.csproj src/SocietyLedger.Infrastructure/
COPY SocietyLedger.Shared/SocietyLedger.Shared.csproj src/SocietyLedger.Shared/

# Restore dependencies
RUN dotnet restore SocietyLedger.sln

# Copy all source files into the expected src/ layout
COPY SocietyLedger.Api/ src/SocietyLedger.Api/
COPY SocietyLedger.Application/ src/SocietyLedger.Application/
COPY SocietyLedger.Domain/ src/SocietyLedger.Domain/
COPY SocietyLedger.Infrastructure/ src/SocietyLedger.Infrastructure/
COPY SocietyLedger.Shared/ src/SocietyLedger.Shared/

# Publish the API project
RUN dotnet publish src/SocietyLedger.Api/SocietyLedger.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Create logs directory
RUN mkdir -p Logs

# Copy published output
COPY --from=build /app/publish .

# Render injects PORT env var; ASP.NET Core reads ASPNETCORE_URLS
ENV ASPNETCORE_URLS=http://+:${PORT:-8080}

EXPOSE 8080

ENTRYPOINT ["dotnet", "SocietyLedger.Api.dll"]
