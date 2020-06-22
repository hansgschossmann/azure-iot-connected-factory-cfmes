FROM mcr.microsoft.com/dotnet/core/sdk:3.1

COPY . /build

WORKDIR /build
RUN dotnet restore
RUN dotnet publish --configuration Release --output /build/out

WORKDIR /build/out
ENTRYPOINT ["dotnet", "/build/out/CfMes.dll"]