FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG CONFIGURATION=Release
ARG TARGETARCH
WORKDIR /src

COPY ["NuGet.config", "."]
COPY ["MaNoir.PlatformOps.sln", "."]
COPY ["eng/Versions.props", "eng/"]
COPY ["apps/MaNoir.PlatformOps.AdminUi/MaNoir.PlatformOps.AdminUi.csproj", "apps/MaNoir.PlatformOps.AdminUi/"]
COPY ["packages/MaNoir.PlatformOps.Core/MaNoir.PlatformOps.Core.csproj", "packages/MaNoir.PlatformOps.Core/"]
COPY ["packages/MaNoir.PlatformOps.Provider.Docker/MaNoir.PlatformOps.Provider.Docker.csproj", "packages/MaNoir.PlatformOps.Provider.Docker/"]
COPY ["packages/MaNoir.PlatformOps.Provider.Kubernetes/MaNoir.PlatformOps.Provider.Kubernetes.csproj", "packages/MaNoir.PlatformOps.Provider.Kubernetes/"]

RUN dotnet restore "apps/MaNoir.PlatformOps.AdminUi/MaNoir.PlatformOps.AdminUi.csproj" -a $TARGETARCH

COPY . .
RUN dotnet publish "apps/MaNoir.PlatformOps.AdminUi/MaNoir.PlatformOps.AdminUi.csproj" --configuration "$CONFIGURATION" --no-restore -a $TARGETARCH -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080

RUN apt-get update \
	&& apt-get install -y --no-install-recommends git ca-certificates \
	&& rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MaNoir.PlatformOps.AdminUi.dll"]