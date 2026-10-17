FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY FoodQueue.slnx ./
COPY src/FoodQueue.Api/FoodQueue.Api.csproj src/FoodQueue.Api/
RUN dotnet restore src/FoodQueue.Api/FoodQueue.Api.csproj
COPY src/FoodQueue.Api/ src/FoodQueue.Api/
RUN dotnet publish src/FoodQueue.Api/FoodQueue.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN mkdir -p /app/uploads
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "FoodQueue.Api.dll"]
