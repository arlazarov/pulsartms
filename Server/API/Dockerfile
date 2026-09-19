FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY ["Server/API/API.csproj", "Server/API/"]
COPY ["Server/Application/Application.csproj", "Server/Application/"]
COPY ["Server/Domain/Domain.csproj", "Server/Domain/"]
COPY ["Server/Infrastructure/Infrastructure.csproj", "Server/Infrastructure/"]

RUN dotnet restore "Server/API/API.csproj" -r linux-x64

COPY . .

RUN dotnet publish "Server/API/API.csproj" \
    -c Release \
    -r linux-x64 \
    --self-contained false \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

ENTRYPOINT ["dotnet", "API.dll"]
