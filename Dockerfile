# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project files first for restore layer caching
COPY SocietyLedger.Api/SocietyLedger.Api.csproj SocietyLedger.Api/
COPY SocietyLedger.Application/SocietyLedger.Application.csproj SocietyLedger.Application/
COPY SocietyLedger.Domain/SocietyLedger.Domain.csproj SocietyLedger.Domain/
COPY SocietyLedger.Infrastructure/SocietyLedger.Infrastructure.csproj SocietyLedger.Infrastructure/
COPY SocietyLedger.Shared/SocietyLedger.Shared.csproj SocietyLedger.Shared/

RUN dotnet restore SocietyLedger.Api/SocietyLedger.Api.csproj

# Copy all source files
COPY SocietyLedger.Api/ SocietyLedger.Api/
COPY SocietyLedger.Application/ SocietyLedger.Application/
COPY SocietyLedger.Domain/ SocietyLedger.Domain/
COPY SocietyLedger.Infrastructure/ SocietyLedger.Infrastructure/
COPY SocietyLedger.Shared/ SocietyLedger.Shared/

RUN dotnet publish SocietyLedger.Api/SocietyLedger.Api.csproj \
    -c Release \
    -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN mkdir -p Logs

COPY --from=build /app/publish .

# Render injects PORT at runtime; the app reads it in Program.cs
EXPOSE 8080

ENTRYPOINT ["dotnet", "SocietyLedger.Api.dll"]
