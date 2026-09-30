# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# This stage is used when running from VS in fast mode (Default for Debug configuration)
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081


# This stage is used to build the service project
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["backend/G4.Api/G4.Api.csproj", "backend/G4.Api/"]
COPY ["backend/G4.Application/G4.Application.csproj", "backend/G4.Application/"]
COPY ["backend/G4.Contracts/G4.Contracts.csproj", "backend/G4.Contracts/"]
COPY ["backend/G4.Domain/G4.Domain.csproj", "backend/G4.Domain/"]
COPY ["backend/G4.Infrastructure/G4.Infrastructure.csproj", "backend/G4.Infrastructure/"]
RUN dotnet restore "./backend/G4.Api/G4.Api.csproj"
COPY . .
WORKDIR "/src/backend/G4.Api"
RUN dotnet build "./G4.Api.csproj" -c $BUILD_CONFIGURATION -o /app/build

# This stage is used to publish the service project to be copied to the final stage
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./G4.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# This stage is used in production or when running from VS in regular mode (Default when not using the Debug configuration)
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "G4.Api.dll"]
