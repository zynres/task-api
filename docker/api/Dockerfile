FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /app

COPY src/TaskApi.csproj src/ 

RUN dotnet restore src/TaskApi.csproj

COPY src/ src/

RUN dotnet publish src/TaskApi.csproj \
    -c Release \
    -o /app/publish \
    --no-restore 

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "TaskApi.dll"]
