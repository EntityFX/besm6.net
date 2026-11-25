# Besm6.Android

Компактное Android-приложение эмулятора БЭСМ-6 на **.NET MAUI**. Машина работает
локально на устройстве, без сервера и без сетевых разрешений. Приложение
использует общие проекты `Besm6.Runtime` и `Besm6.Assembler`; интерфейс написан
на C# и XAML с MVVM.

## Требования

- .NET SDK 10 с рабочими нагрузками `android` и `maui-android`;
- JDK 21 (текущий Android workload не принимает JDK 22+);
- Android SDK с платформой API 36;
- минимальная версия Android — **10 (API 29)**.

Пример локальной сборки:

```powershell
$env:DOTNET_ROOT = "$HOME\.dotnet10"
$env:JAVA_HOME = "C:\path\to\jdk-21"
$env:ANDROID_HOME = "$env:LOCALAPPDATA\Android\Sdk"
dotnet build src/Besm6.Android/Besm6.Android.csproj -c Debug `
  -p:JavaSdkDirectory=$env:JAVA_HOME `
  -p:AndroidSdkDirectory=$env:ANDROID_HOME
```

Проект намеренно не входит в общий `Besm6.sln`: обычная CI-сборка не должна
требовать Android/MAUI workload. Для Android используется отдельный
`src/Besm6.Android/Besm6.Android.sln`.

Standalone Debug APK (со встроенными сборками и ресурсами):

```powershell
dotnet build src/Besm6.Android/Besm6.Android.csproj -c Debug `
  -f net10.0-android36.0 -p:EmbedAssembliesIntoApk=true
```

Release APK для телефона (`arm64-v8a`):

```powershell
dotnet publish src/Besm6.Android/Besm6.Android.csproj -c Release `
  -f net10.0-android36.0 `
  -p:AndroidPackageFormat=apk `
  -p:EmbedAssembliesIntoApk=true `
  -p:RunAOTCompilation=false
```

`RunAOTCompilation=false` нужен, потому что проект собирается без trimming;
включать AOT следует только вместе с проверенной обрезкой.

Готовый файл: `src/Besm6.Android/bin/Release/net10.0-android36.0/publish/ru.besm6.android-Signed.apk`.
Для установки на телефон используется debug-подпись; для публикации в магазине
нужен собственный keystore.

## Устройство

| Часть | Назначение |
|---|---|
| `MainPage.xaml` | Компактный экран с вкладками «Код / Поиск / Регистры / Память / Терминал / Печать / Карты / График» |
| `ViewModels/MainViewModel.cs` | Состояние экрана, вкладки, команды и окно настроек |
| `Services/MachineRunService.cs` | Локальная сессия: загрузка, выполнение, пауза/шаг, остановка, ввод и снимки |
| `Services/CodeSearchService.cs` | Поиск по именам `.dub` и строкам исходного кода |
| `Services/AssetInstaller.cs` | Распаковка лент и примеров из APK во внутреннее хранилище |
| `Services/RegisterFormatter.cs` | Текстовое представление M0–M15, БРЧ, БРС, БРЗ и памяти |
| `Services/PlotterParser.cs` | Декодирование команд Watanabe, Calcomp и Tektronix |
| `Ui/SyntaxEditor.cs` | Подсветка Fortran/Pascal/ассемблера в нативном редакторе |
| `Mvvm` | Минимальные `INotifyPropertyChanged`, `ICommand` и UI-dispatch |

## Поведение

- Встроенные ленты и примеры `.dub` копируются в приватный каталог приложения
  при первом запуске и после обновления пакета.
- Перед запуском текущий текст редактора сохраняется в рабочий `job.dub`.
- Выполнение идёт в фоновом потоке. UI получает только независимые снимки
  регистров и фрагменты вывода устройств.
- Снимки регистров обновляются не чаще раза в секунду; завершение и ошибки
  показываются сразу.
- **M0–M15** отображаются всегда. **БРС/БРЗ** доступны в буферной и Mapped-моделях.
  **БРЧ** доступны только в аппаратной модели supervisor; в остальных режимах
  окно явно сообщает, что регистр не моделируется, и не подставляет нули.
- Вывод устройств сохраняется в ограниченном буфере. Переключение вкладок показывает
  регистры, память, терминал, печать, перфокарты и график на месте редактора.
- Терминал принимает строки, когда программа запросила ввод; вкладка терминала
  открывается автоматически.
- Поиск ищет по имени файла и содержимому `.dub` и открывает найденный исходник.
- В режиме отладки запуск начинается с паузы; доступны «Пауза», «Шаг» и
  «Продолжить». Настройки запуска остаются в отдельном модальном окне.
- Светлая, тёмная и системная темы переключаются в нижней панели и сохраняются.

## Иконка

Иконка соединяет ламповую матрицу «Б-6» с пультом БЭСМ-6 и серый
hexagonal-бейдж .NET. Палитра — серый, коричневый и красный.
Исходники: `Resources/AppIcon` и `Resources/Splash`.
