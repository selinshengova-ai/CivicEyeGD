FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["CivicEyeGD.csproj", "./"]
RUN dotnet restore "CivicEyeGD.csproj"

COPY . .
RUN dotnet publish "CivicEyeGD.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# PDF отчетът се рисува с QuestPDF, а той ползва freetype и fontconfig
# за буквите. В базовия образ на ASP.NET двете библиотеки ги няма: без
# тях докладът спира още с първия ред — грешка, която на собствения
# компютър не се вижда, защото Windows вече има шрифтите.
RUN apt-get update \
    && apt-get install -y --no-install-recommends fontconfig libfreetype6 \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "CivicEyeGD.dll"]
