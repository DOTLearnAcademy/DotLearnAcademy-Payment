FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
EXPOSE 80

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ["DotLearn.Payment/DotLearn.Payment.csproj", "DotLearn.Payment/"]
RUN dotnet restore "DotLearn.Payment/DotLearn.Payment.csproj"
COPY . .
WORKDIR "/src/DotLearn.Payment"
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "DotLearn.Payment.dll"]
