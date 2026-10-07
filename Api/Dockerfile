FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY Trader.Protocol/Trader.Protocol.csproj Trader.Protocol/
COPY Domain/Domain.csproj Domain/
COPY Application/Application.csproj Application/
COPY Infrastructure/Infrastructure.csproj Infrastructure/
COPY Api/Api.csproj Api/
RUN dotnet restore Api/Api.csproj
COPY . .
RUN dotnet publish Api/Api.csproj -c Release --no-restore -o /app/publish /p:UseAppHost=false
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
RUN mkdir -p /app/data && chown app:app /app/data
ENV ASPNETCORE_URLS=http://+:8080
ENV Runtime__DataDirectory=/app/data
ENV DOTNET_EnableDiagnostics=0
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "Api.dll"]
