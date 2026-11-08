# Кредити и лицензи

Този файл отговаря на един въпрос, който един честен разработчик дължи на
читателя: **което виждате в `CivicEyeGD` е собствен труд на автора и нищо
не се представя за такова, ако не е.** Всичко, докоснато от друг, е
подписано с името, версията и лиценза на този друг.

Приложението е с **GNU General Public License v3.0** — текстът е в
[`LICENSE`](LICENSE). Кодът, събран в `Pages/`, `Services/`, `Data/`,
`wwwroot/css/`, `wwwroot/js/`, `Migrations/`, `Tests/` и `Tools/`, е
написан от **[Селин Красимирова Шенгова](https://github.com/selinshengova-ai)**
за състезанието НОИТ 2026/2027, направление „Софтуерни приложения“, с
научен ръководител г-жа Караахмед (ПМГ „Яне Сандански“, гр. Гоце Делчев).

Единственото изключение от „написано от нулата“ е скелетът: проектът
започва като шаблонен ASP.NET Core Razor Pages проект, генериран от
`dotnet new`, чиито празни страници, `_Layout.cshtml` и Identity настройки
постепенно са презаписани. Това е стандартна практика и е честно да се
каже.

## Библиотеки, с които работи приложението

| Компонент | Версия | Лиценз | За какво служи в CivicEye GD |
|---|---|---|---|
| [.NET](https://dotnet.microsoft.com/) / ASP.NET Core | 10.0 | MIT | Уеб сървърът, Razor Pages, Identity автентикацията и ролята „Администратор“ |
| [Entity Framework Core](https://learn.microsoft.com/ef/core/) | 10.0.13 | MIT | Достъп до базата данни и миграциите |
| [Microsoft.EntityFrameworkCore.Sqlite](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.Sqlite) | 10.0.13 | MIT | Съхранение в SQLite — файл, който може да се копира |
| [Microsoft.EntityFrameworkCore.SqlServer](https://www.nuget.org/packages/Microsoft.EntityFrameworkCore.SqlServer) | 10.0.13 | MIT | Алтернативен доставчик за хостинг със SQL Server |
| [Npgsql.EntityFrameworkCore.PostgreSQL](https://www.nuget.org/packages/Npgsql.EntityFrameworkCore.PostgreSQL) | 10.0.2 | [PostgreSQL License](https://www.postgresql.org/about/licence/) | Алтернативен доставчик за PostgreSQL |
| [QuestPDF](https://www.questpdf.com/) | 2026.9.1 | Собствен, двоен: безплатен Community лиценз v3.0 (не е OSI-лиценз; свободен за физически лица и организации с оборот под 1 000 000 USD, за некомърсиални и учебни проекти; изходния код на библиотеката не се разпространява), професионалният и корпоративният се купуват | PDF отчетът с диаграмите — генерира се на сървъра с `LicenseType.Community` |
| [ClosedXML](https://github.com/ClosedXML/ClosedXML) | 0.105.1 | MIT | .xlsx експортът от администраторското табло |
| [jQuery](https://jquery.com/) | 3.7.1 (`wwwroot/lib/jquery`) | MIT | Взаимодействие в формите и менюто |
| [Bootstrap](https://getbootstrap.com/) | 5.3.3 (`wwwroot/lib/bootstrap`) | MIT | Решетка, форми и падащи менюта |
| [jQuery Validation](https://github.com/jquery-validation/jquery-validation) | 1.21.0 (`wwwroot/lib/jquery-validation`) | MIT | Проверка на формите в браузъра |
| [jQuery Validation Unobtrusive](https://github.com/aspnet/jquery-validation-unobtrusive) | 4.0.0 (`wwwroot/lib/jquery-validation-unobtrusive`) | MIT | Мост между атрибутите на C# модела и проверката в браузъра |
| [Leaflet](https://leafletjs.com/) | 1.9.4 | [BSD-2-Clause](https://github.com/Leaflet/Leaflet/blob/main/LICENSE) | Картата със сигналите: `Map.cshtml`, `Create.cshtml`, `Details.cshtml` |

## Данни и услуги от трети страни

| Източник | Лиценз / условия | Какво се взима от него |
|---|---|---|
| [OpenStreetMap](https://www.openstreetmap.org/copyright) | Данните — ODbL; плочките — CC BY-SA 2.0 | Фоновите плочки на картата. Авторството се изписва в `attribution`-а на Leaflet, където е задължително по условията на OSM. |
| [OpenStreetMap Nominatim](https://nominatim.org/) | Данните — ODbL; употреба по [политиката](https://usage-policy.nominatim.openstreetmap.org/) | Превръщане на въведения адрес в координати във формуляра за нов сигнал. |
| [Google Maps Platform](https://mapsplatform.google.com/policies/terms/) | Собствен, по условията на Google | Само панорамният изглед „Street View“ към координатите на сигнала — `google.maps.StreetViewPanorama` в `Create.cshtml`. Геокодирането не минава през Google, а през Nominatim. Изисква API ключ, който се чете от настройката `GoogleMapsApi:ApiKey` (user secrets при разработка, променлива на средата при хостинг); **ключът не е в репозиторито.** |

## Шрифтове и икони

Приложението не сваля и не разполага шрифтове. CSS-ът моли за
`"Segoe UI", Inter, Arial, sans-serif` — системни шрифтове, които вече са
на машината, а иконите са текстови символи (↗, +, ×) и Unicode стрелки.
Така няма третостранен шрифтов лиценз, за който да се отчитаме, и няма
допълнително изтегляне при зареждане на страница.

## Инструменти, с които е направено

- **.NET SDK 10** и `dotnet` CLI — изграждане, тестове, локален сървър.
- **[xUnit](https://xunit.net/)** 2.9.3 (Apache-2.0) и `Microsoft.NET.Test.Sdk`
  (MIT) — 288 автоматизирани теста в `Tests/CivicEyeGD.Tests`.
- **[QRCoder](https://github.com/Shane32/QRCoder)** 1.8.1 (MIT) — генерира QR
  кода към `civiceye.runasp.net` за флаера и за последния кадър на
  презентацията. Библиотеката не е зависимост на приложението: пусната е
  веднъж извън проекта, а резултатът ѝ е файлът
  `Docs/media/qr-civiceye-gd.png`.
- **[ZXing.Net](https://github.com/micjahn/ZXing.Net)** 0.16.11 (Apache-2.0)
  — с него кодът е прочетен обратно от изображението, за да се провери, че
  отива точно където трябва. Проверката е извън тестовете на приложението.
- **[Git](https://git-scm.com/)** и **GitHub** — история на промените и
  публичният профил на проекта.
- **GitHub Actions** (`.github/workflows/`) — пускане на тестовете при
  всяко изпращане.
- **Microsoft Edge** и неговият конзол — ръчна проверка в браузъра.

## Какво не е използвано

Няма генеративен модел, няма готов „admin panel“ шаблон, няма чужда
база данни с реални сигнали на община. Демо сигналите са написани от
автора в `Data/DbSeeder.cs` и са изрично обозначени като демонстрационни.

## Ако преизползвате проекта

GPL v3.0 позволява да копирвате, променяте и разпространявате, стига
производните творби да останат под същия лиценз и да запазите този файл.
Ако преизползвате PDF отчета в собствен продукт, обърнете внимание на
Community лиценза на QuestPDF: той е безплатен за индивидуална и
учебна употреба, за организации с годишен оборот под 1 000 000 щатски
долара и за проекти под лиценз, одобрен от OSI — какъвто е GPL v3 на
този проект. При съмнение се чете
[документът за лицензиране](https://www.questpdf.com/pricing), а не
тълкуването в тази таблица.
Плочките на OpenStreetMap задължават да се изписва
„© OpenStreetMap contributors“ там, където се виждат.

---

*Файлът е част от проекта CivicEye GD. Последна проверка на версиите и
лицензите: 08.11.2026 г., спрямо `CivicEyeGD.csproj`,
`Tests/CivicEyeGD.Tests/CivicEyeGD.Tests.csproj` и лицензите, доставени с
всяка библиотека.*
