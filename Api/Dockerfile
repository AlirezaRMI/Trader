FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY ["Api/Api.csproj", "Trader.Api/"]
COPY ["Application/Application.csproj", "Trader.Application/"]
COPY ["Domain/Domain.csproj", "Trader.Domain/"]
COPY ["Infrastructure/Infrastructure.csproj", "Trader.Infrastructure/"]

COPY . .

WORKDIR /src/Api 
RUN dotnet restore "Api.csproj"

RUN dotnet build "Api.csproj" -c Release -o /app/build


FROM build AS publish
WORKDIR /src/Api
RUN dotnet publish "Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

COPY --from=publish /app/publish .


EXPOSE 8080

ENTRYPOINT ["dotnet", "Api.dll"]