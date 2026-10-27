FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
RUN apt-get update \
    && apt-get install -y --no-install-recommends python3 \
    && rm -rf /var/lib/apt/lists/*
RUN dotnet workload install wasm-tools
WORKDIR /source
COPY . .
RUN dotnet publish Q.Sock.Browser/Q.Sock.Browser.csproj -c Release -m:1 -nr:false
RUN mkdir -p Q.Sock.Server/wwwroot/avalonia \
    && cp -a Q.Sock.Browser/bin/Release/net10.0-browser/publish/wwwroot/. Q.Sock.Server/wwwroot/avalonia/
RUN dotnet publish Q.Sock.Server/Q.Sock.Server.csproj -c Release -o /output -m:1 -nr:false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /output .
RUN mkdir -p /app/data /app/state /app/storage \
    && chown -R app:app /app/data /app/state /app/storage
USER app
ENV ASPNETCORE_HTTP_PORTS=8080
ENV ConnectionStrings__ChatDatabase="Data Source=/app/data/chat.db"
EXPOSE 8080
ENTRYPOINT ["dotnet", "Q.Sock.Server.dll"]
