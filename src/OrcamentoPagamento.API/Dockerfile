# Imagem base de execução.
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER root

# Dependências nativas exigidas pelo QuestPDF/SkiaSharp no Linux. A fonte DejaVu é a
# usada no PDF do orçamento (page.DefaultTextStyle), por isso vem instalada aqui.
RUN apt-get update && apt-get install -y --no-install-recommends \
    libfontconfig1 \
    libfreetype6 \
    fontconfig \
    fonts-dejavu-core \
    curl \
    && fc-cache -f \
    && rm -rf /var/lib/apt/lists/*

USER $APP_UID
WORKDIR /app
EXPOSE 8080

# Compilação.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Copia só os .csproj primeiro para aproveitar o cache do restore.
COPY ["src/OrcamentoPagamento.API/OrcamentoPagamento.API.csproj", "src/OrcamentoPagamento.API/"]
COPY ["src/OrcamentoPagamento.Application/OrcamentoPagamento.Application.csproj", "src/OrcamentoPagamento.Application/"]
COPY ["src/OrcamentoPagamento.Domain/OrcamentoPagamento.Domain.csproj", "src/OrcamentoPagamento.Domain/"]
COPY ["src/OrcamentoPagamento.Infrastructure/OrcamentoPagamento.Infrastructure.csproj", "src/OrcamentoPagamento.Infrastructure/"]

RUN dotnet restore "src/OrcamentoPagamento.API/OrcamentoPagamento.API.csproj"

COPY . .

WORKDIR "/src/src/OrcamentoPagamento.API"
RUN dotnet build "OrcamentoPagamento.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "OrcamentoPagamento.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish --chown=$APP_UID:$APP_UID /app/publish .
# Declarado explicitamente (mesmo já herdado de "base") para que scanners de
# segurança confirmem que a imagem final não roda como root.
USER $APP_UID
ENTRYPOINT ["dotnet", "OrcamentoPagamento.API.dll"]
