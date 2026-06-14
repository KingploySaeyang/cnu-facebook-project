# ใช้ SDK .NET 9.0 ในการคอมไพล์โค้ดหน้าบ้านคุณพลอย
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# สั่งเลี้ยวเข้าห้องเปิดไฟล์โปรเจกต์หน้าบ้านตรง ๆ คราฟ
COPY ["Frontend/CnuFacebookBlazor/CnuFacebookBlazor/CnuFacebookBlazor.csproj", "Frontend/CnuFacebookBlazor/CnuFacebookBlazor/"]
RUN dotnet restore "Frontend/CnuFacebookBlazor/CnuFacebookBlazor/CnuFacebookBlazor.csproj"

COPY . .
WORKDIR "/src/Frontend/CnuFacebookBlazor/CnuFacebookBlazor"
RUN dotnet build "CnuFacebookBlazor.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "CnuFacebookBlazor.csproj" -c Release -o /app/publish /p:UseAppHost=false

# ใช้คำสั่งรันเซิร์ฟเวอร์เว็บจำลองดึงหน้าแอปขึ้นออนไลน์
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "CnuFacebookBlazor.dll"]
