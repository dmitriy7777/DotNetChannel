FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Channel.Worker/Channel.Worker.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
COPY settings.example.json .
COPY prompts ./prompts
ENTRYPOINT ["dotnet", "Channel.Worker.dll", "--root", "/app"]
