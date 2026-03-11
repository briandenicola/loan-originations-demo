# ── Build stage ───────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

# Restore as a separate layer for caching
COPY src/workflow/LoanOrigination.csproj ./src/workflow/
RUN dotnet restore src/workflow/LoanOrigination.csproj --runtime linux-x64

# Copy application source and materials data
COPY src/workflow/ ./src/workflow/
COPY materials/ ./materials/
COPY sample-data/ ./sample-data/

RUN dotnet publish src/workflow/LoanOrigination.csproj -c Release \
    --runtime linux-x64 \
    --self-contained false \
    --no-restore \
    -o /app/publish \
    /p:PublishTrimmed=false

# Copy materials into publish output (app expects ../../materials/data relative to cwd)
RUN mkdir -p /app/materials/data && cp -r ./materials/data/* /app/materials/data/

# ── Runtime stage ─────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final

USER $APP_UID
WORKDIR /app
EXPOSE 8080

COPY --from=build /app/publish .
COPY --from=build /app/materials ../materials

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_EnableDiagnostics=0

HEALTHCHECK --interval=30s --timeout=5s --start-period=10s --retries=3 \
    CMD ["dotnet", "LoanOrigination.dll", "--urls", "http://localhost:8080/health" ]

ENTRYPOINT ["dotnet", "LoanOrigination.dll"]
