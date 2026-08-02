# syntax=docker/dockerfile:1.7
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Summa.Fiscal.sln
RUN dotnet publish src/Summa.Fiscal.Api/Summa.Fiscal.Api.csproj -c Release -o /out/api --no-restore /p:UseAppHost=false
RUN dotnet publish src/Summa.Fiscal.Worker/Summa.Fiscal.Worker.csproj -c Release -o /out/worker --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS api
WORKDIR /app
RUN apt-get update \
    && apt-get install --no-install-recommends -y curl \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system --gid 10001 summa \
    && useradd --system --uid 10001 --gid summa --no-create-home summa
COPY --from=build --chown=summa:summa /out/api/ .
COPY --chown=summa:summa deploy/docker-entrypoint.sh /usr/local/bin/summa-entrypoint
RUN chmod 0555 /usr/local/bin/summa-entrypoint
USER summa
EXPOSE 8080
ENTRYPOINT ["/usr/local/bin/summa-entrypoint"]
CMD ["dotnet", "Summa.Fiscal.Api.dll"]

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS worker
WORKDIR /app
RUN apt-get update \
    && apt-get install --no-install-recommends -y libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --system --gid 10001 summa \
    && useradd --system --uid 10001 --gid summa --no-create-home summa
COPY --from=build --chown=summa:summa /out/worker/ .
COPY --chown=summa:summa deploy/docker-entrypoint.sh /usr/local/bin/summa-entrypoint
RUN chmod 0555 /usr/local/bin/summa-entrypoint
USER summa
ENTRYPOINT ["/usr/local/bin/summa-entrypoint"]
CMD ["dotnet", "Summa.Fiscal.Worker.dll"]
