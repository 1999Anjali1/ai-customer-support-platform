# ---------- build ----------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (cached layer as long as project files don't change)
COPY Directory.Build.props SmartSupport.slnx ./
COPY src/SmartSupport.Domain/SmartSupport.Domain.csproj src/SmartSupport.Domain/
COPY src/SmartSupport.Application/SmartSupport.Application.csproj src/SmartSupport.Application/
COPY src/SmartSupport.Infrastructure/SmartSupport.Infrastructure.csproj src/SmartSupport.Infrastructure/
COPY src/SmartSupport.Api/SmartSupport.Api.csproj src/SmartSupport.Api/
RUN dotnet restore src/SmartSupport.Api/SmartSupport.Api.csproj

COPY src/ src/
RUN dotnet publish src/SmartSupport.Api/SmartSupport.Api.csproj -c Release -o /app --no-restore

# ---------- runtime ----------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "SmartSupport.Api.dll"]
