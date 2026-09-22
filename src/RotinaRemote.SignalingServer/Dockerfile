FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["src/RotinaRemote.Core/RotinaRemote.Core.csproj", "src/RotinaRemote.Core/"]
COPY ["src/RotinaRemote.Protocol/RotinaRemote.Protocol.csproj", "src/RotinaRemote.Protocol/"]
COPY ["src/RotinaRemote.SignalingServer/RotinaRemote.SignalingServer.csproj", "src/RotinaRemote.SignalingServer/"]

RUN dotnet restore "src/RotinaRemote.SignalingServer/RotinaRemote.SignalingServer.csproj"

COPY src/ src/

WORKDIR "/src/src/RotinaRemote.SignalingServer"
RUN dotnet publish "RotinaRemote.SignalingServer.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV PORT=5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "RotinaRemote.SignalingServer.dll"]
