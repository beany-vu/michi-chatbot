FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore from csproj files only, so the package cache layer survives source edits.
COPY src/MichiChatbot.Core/MichiChatbot.Core.csproj src/MichiChatbot.Core/
COPY src/MichiChatbot.Infrastructure/MichiChatbot.Infrastructure.csproj src/MichiChatbot.Infrastructure/
COPY src/MichiChatbot.Web/MichiChatbot.Web.csproj src/MichiChatbot.Web/
RUN dotnet restore src/MichiChatbot.Web/MichiChatbot.Web.csproj

COPY src/ src/
RUN dotnet publish src/MichiChatbot.Web/MichiChatbot.Web.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MichiChatbot.Web.dll"]
