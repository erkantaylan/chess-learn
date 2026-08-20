# Repertoire Table: the .NET API plus the static frontend in one image.
#
# Build context is the REPO ROOT (see docker-compose.yml) — the image has to copy index.html,
# login.html, views/ and engine/ as well as the C# projects.
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER root
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# csproj + central versions first, so `dotnet restore` caches unless dependencies actually change.
COPY ["Directory.Packages.props", "./"]
COPY ["src/Api/Repertoire.Api/Repertoire.Api.csproj", "src/Api/Repertoire.Api/"]
COPY ["src/Aspire/Repertoire.ServiceDefaults/Repertoire.ServiceDefaults.csproj", "src/Aspire/Repertoire.ServiceDefaults/"]
RUN dotnet restore "src/Api/Repertoire.Api/Repertoire.Api.csproj"
COPY . .
WORKDIR "/src/src/Api/Repertoire.Api"
RUN dotnet build "./Repertoire.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./Repertoire.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
# The frontend. No build step — these are the files as they sit in the repo.
COPY --from=build /src/index.html ./wwwroot/index.html
COPY --from=build /src/login.html ./wwwroot/login.html
COPY --from=build /src/views ./wwwroot/views
COPY --from=build /src/engine ./wwwroot/engine
ENV StaticApp__Root=/app/wwwroot
ENTRYPOINT ["dotnet", "Repertoire.Api.dll"]
