# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files for restore layer caching
COPY SocietyLedger.Api/SocietyLedger.Api.csproj src/SocietyLedger.Api/
COPY SocietyLedger.Application/SocietyLedger.Application.csproj src/SocietyLedger.Application/
COPY SocietyLedger.Domain/SocietyLedger.Domain.csproj src/SocietyLedger.Domain/
COPY SocietyLedger.Infrastructure/SocietyLedger.Infrastructure.csproj src/SocietyLedger.Infrastructure/
COPY SocietyLedger.Shared/SocietyLedger.Shared.csproj src/SocietyLedger.Shared/

# Restore only the API project (avoids missing test project in .sln)
RUN dotnet restore src/SocietyLedger.Api/SocietyLedger.Api.csproj

# Copy all source files into the expected src/ layout
COPY SocietyLedger.Api/ src/SocietyLedger.Api/
COPY SocietyLedger.Application/ src/SocietyLedger.Application/
COPY SocietyLedger.Domain/ src/SocietyLedger.Domain/
COPY SocietyLedger.Infrastructure/ src/SocietyLedger.Infrastructure/
COPY SocietyLedger.Shared/ src/SocietyLedger.Shared/

# Publish the API project
RUN dotnet publish src/SocietyLedger.Api/SocietyLedger.Api.csproj \
    -c Release \
    -o /app/publish

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
