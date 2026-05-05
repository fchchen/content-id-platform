FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/ContentId.Api/ContentId.Api.csproj src/ContentId.Api/
RUN dotnet restore src/ContentId.Api/ContentId.Api.csproj
COPY . .
RUN dotnet publish src/ContentId.Api/ContentId.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
COPY data/reference-assets ./data/reference-assets
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "ContentId.Api.dll"]
