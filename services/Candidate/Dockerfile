# Build context: services/ (docker compose sets it). Multi-stage: SDK to build, ASP.NET runtime to run.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY BuildingBlocks/src/ BuildingBlocks/src/
COPY Candidate/src/ Candidate/src/
RUN dotnet publish Candidate/src/Recuro.Candidate.Api/Recuro.Candidate.Api.csproj -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# CVs are stored encrypted on a volume mounted here (ResumeStorage:RootPath).
RUN mkdir -p /data/resumes && chown $APP_UID /data/resumes
ENV ResumeStorage__RootPath=/data/resumes
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "Recuro.Candidate.Api.dll"]
