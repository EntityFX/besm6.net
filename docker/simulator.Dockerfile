FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /source
COPY Directory.Build.props ./
COPY src/Besm6.Architecture/ src/Besm6.Architecture/
COPY src/Besm6.Processor/ src/Besm6.Processor/
COPY src/Besm6.Assembler/ src/Besm6.Assembler/
COPY src/Besm6.Runtime/ src/Besm6.Runtime/
COPY src/Besm6.AspNetCore.Web/ src/Besm6.AspNetCore.Web/
COPY examples/ examples/
COPY tapes/ tapes/
RUN dotnet publish src/Besm6.AspNetCore.Web/Besm6.AspNetCore.Web.csproj \
    --configuration Release --output /app/publish --self-contained false /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app/publish ./
ENV ASPNETCORE_HTTP_PORTS=8080
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Besm6.AspNetCore.Web.dll"]
