# =========================
# ETAPA 1: BUILD
# =========================
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copiar todo el proyecto
COPY . .

# Restaurar dependencias
RUN dotnet restore "FisioMarca.web/FisioMarcaweb.csproj"

# Publicar en modo Release
RUN dotnet publish "FisioMarca.web/FisioMarcaweb.csproj" -c Release -o /app/publish /p:UseAppHost=false


# =========================
# ETAPA 2: RUNTIME
# =========================
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Render usa este puerto (puedes dejar 10000)
ENV ASPNETCORE_URLS=http://+:10000
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 10000

# Copiar archivos publicados
COPY --from=build /app/publish .

# Iniciar la app
ENTRYPOINT ["dotnet", "FisioMarcaweb.dll"]