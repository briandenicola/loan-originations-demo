# ── Build stage ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Restore as a separate layer for caching
COPY src/LoanOriginationDemo.csproj .
RUN dotnet restore --runtime linux-x64

# Copy source and publish
COPY src/ .
RUN dotnet publish -c Release \
    --runtime linux-x64 \
    --self-contained false \
    --no-restore \
    -o /app/publish \
    /p:PublishTrimmed=false

# ── Runtime stage ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final

# Run as non-root (chiseled images use app user by default)
USER $APP_UID
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD ["dotnet", "LoanOriginationDemo.dll", "--urls", "http://localhost:8080/health" ]

ENTRYPOINT ["dotnet", "LoanOriginationDemo.dll"]
