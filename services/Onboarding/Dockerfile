# Build context: services/ (docker compose sets it). Multi-stage: SDK to build, ASP.NET runtime to run.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY BuildingBlocks/src/ BuildingBlocks/src/
COPY Onboarding/src/ Onboarding/src/
RUN dotnet publish Onboarding/src/Recuro.Onboarding.Api/Recuro.Onboarding.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# §13 documents are stored encrypted on a volume mounted here (DocumentStorage:RootPath).
RUN mkdir -p /data/onboarding-documents && chown $APP_UID /data/onboarding-documents
ENV DocumentStorage__RootPath=/data/onboarding-documents
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Recuro.Onboarding.Api.dll"]
