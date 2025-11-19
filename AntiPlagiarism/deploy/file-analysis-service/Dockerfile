FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY FileAnalysisService/FileAnalysisService.csproj FileAnalysisService/
COPY Shared/Shared.csproj Shared/
RUN dotnet restore FileAnalysisService/FileAnalysisService.csproj

COPY . .
RUN dotnet publish FileAnalysisService/FileAnalysisService.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "FileAnalysisService.dll"]
