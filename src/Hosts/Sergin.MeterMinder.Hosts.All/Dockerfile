# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER app
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy the whole source tree: this is a modular monolith whose host project
# references the module/SharedKernel projects and relies on Directory.Build.props,
# so a restore needs every referenced .csproj present.
COPY . .
RUN dotnet restore "src/Hosts/Sergin.MeterMinder.Hosts.All/Sergin.MeterMinder.Hosts.All.csproj"
RUN dotnet build "src/Hosts/Sergin.MeterMinder.Hosts.All/Sergin.MeterMinder.Hosts.All.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "src/Hosts/Sergin.MeterMinder.Hosts.All/Sergin.MeterMinder.Hosts.All.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Sergin.MeterMinder.Hosts.All.dll"]
