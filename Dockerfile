# 建置階段 (Build Stage)
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY WebApplication1/*.csproj ./WebApplication1/
RUN dotnet restore WebApplication1/WebApplication1.csproj
COPY . .
RUN dotnet publish WebApplication1/WebApplication1.csproj -c Release -o /app/out

# 執行階段 (Runtime Stage)
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/out .
EXPOSE 8080
ENTRYPOINT ["dotnet", "WebApplication1.dll"]