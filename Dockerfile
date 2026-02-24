# =========================
# ETAPA 1: BUILD
# =========================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .

RUN dotnet restore "FisioMarca.web/FisioMarcaweb.csproj"
RUN dotnet publish "FisioMarca.web/FisioMarcaweb.csproj" -c Release -o /app/publish /p:UseAppHost=false

# =========================
# ETAPA 2: RUNTIME
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish .

# Render inyecta PORT en runtime; si no existe, usa 10000
ENTRYPOINT ["sh", "-c", "ASPNETCORE_URLS=http://+:${PORT:-10000} dotnet FisioMarcaweb.dll"]