# Build context: services/ (docker compose sets it). Multi-stage: SDK to build, ASP.NET runtime to run.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY BuildingBlocks/src/ BuildingBlocks/src/
COPY Reporting/src/ Reporting/src/
RUN dotnet publish Reporting/src/Recuro.Reporting.Api/Recuro.Reporting.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Recuro.Reporting.Api.dll"]
