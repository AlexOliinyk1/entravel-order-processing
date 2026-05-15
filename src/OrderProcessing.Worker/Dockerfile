# syntax=docker/dockerfile:1.7
# Build context = repo root (see docker-compose.yml: build.context = .)

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY src/OrderProcessing.Shared/ src/OrderProcessing.Shared/
COPY src/OrderProcessing.Worker/ src/OrderProcessing.Worker/

RUN dotnet restore src/OrderProcessing.Worker/OrderProcessing.Worker.csproj
RUN dotnet publish src/OrderProcessing.Worker/OrderProcessing.Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish ./

ENV DOTNET_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "OrderProcessing.Worker.dll"]
