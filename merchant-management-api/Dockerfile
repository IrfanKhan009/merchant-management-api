FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/MerchantManagement.Api/MerchantManagement.Api.csproj src/MerchantManagement.Api/
RUN dotnet restore src/MerchantManagement.Api/MerchantManagement.Api.csproj
COPY src/ src/
RUN dotnet publish src/MerchantManagement.Api/MerchantManagement.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app .
RUN mkdir /data && chown app /data
USER app
ENV ASPNETCORE_URLS=http://+:8080 \
    ConnectionStrings__Default="Data Source=/data/merchants.db"
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "MerchantManagement.Api.dll"]
