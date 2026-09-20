FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["src/FlashSale.Domain/FlashSale.Domain.csproj", "src/FlashSale.Domain/"]
COPY ["src/FlashSale.Application/FlashSale.Application.csproj", "src/FlashSale.Application/"]
COPY ["src/FlashSale.Infrastructure/FlashSale.Infrastructure.csproj", "src/FlashSale.Infrastructure/"]
COPY ["src/Order.Api/Order.Api.csproj", "src/Order.Api/"]
RUN dotnet restore "src/Order.Api/Order.Api.csproj"
COPY . .
WORKDIR "/src/src/Order.Api"
RUN dotnet publish "Order.Api.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
EXPOSE 8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Order.Api.dll"]
