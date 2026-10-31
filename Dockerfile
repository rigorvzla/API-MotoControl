# syntax=docker/dockerfile:1

# ------------------------------------------------------------
#  MotoControl API - imagen para Render / cualquier host Docker
# ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Restaurar primero (mejor cache de capas)
COPY MotoControl.Shared/MotoControl.Shared.csproj MotoControl.Shared/
COPY MotoControl.Api/MotoControl.Api.csproj MotoControl.Api/
RUN dotnet restore MotoControl.Api/MotoControl.Api.csproj

# Codigo y publicacion
COPY MotoControl.Shared/ MotoControl.Shared/
COPY MotoControl.Api/ MotoControl.Api/
RUN dotnet publish MotoControl.Api/MotoControl.Api.csproj \
    -c Release \
    -o /app/publish \
    /p:UseAppHost=false

# ------------------------------------------------------------
#  Runtime
# ------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# QuestPDF/Skia necesita fuentes y fontconfig en Linux.
# tzdata permite resolver "America/Caracas".
RUN apt-get update \
    && apt-get install -y --no-install-recommends \
        libfontconfig1 \
        libfreetype6 \
        fonts-dejavu-core \
        tzdata \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

COPY --from=build /app/publish ./

# Render inyecta $PORT (10000 por defecto); en local se usa 10000.
EXPOSE 10000
ENTRYPOINT ["sh", "-c", "exec dotnet MotoControl.Api.dll --urls http://0.0.0.0:${PORT:-10000}"]
