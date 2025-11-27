FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /app

COPY . .
RUN dotnet restore
RUN dotnet publish src/DimonSmart.LocalOllamaMCPServer/DimonSmart.LocalOllamaMCPServer.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/runtime:8.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DimonSmart.LocalOllamaMCPServer.dll"]
