FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app
COPY *.csproj ./
RUN dotnet restore
COPY . ./
RUN dotnet publish -c Release -o out

FROM mcr.microsoft.com/dotnet/runtime:10.0
RUN apt-get update && apt-get install -y \
    libfontconfig1 \
    libfreetype6 \
    libkrb5-3 \
    ffmpeg \
    libopus0 \
    libsodium23 \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/out ./
ENTRYPOINT ["dotnet", "Botzinho.dll"]
