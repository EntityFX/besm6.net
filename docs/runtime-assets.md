# Runtime-ресурсы (ленты БЭСМ-6): поставка, проверка, установка

> SuperPlan Task A4 — runtime-ресурсы как явная часть поставки, без скрытой зависимости от
> developer junction `ref/dubna`.

## Какие образы нужны

Исполнение MONSYS/CERN-нагрузки требует пять лент. Каталог (checksum-manifest) задаётся в
`src/besm6.net/Loader/RuntimeAssets.cs` (`Besm6.Loader.RuntimeAssets.Catalog`) и включает имя,
тап-узел, ожидаемый SHA256, происхождение, класс лицензии и способ получения.

| Файл       | Узел | Происхождение                                        | Лицензия       |
|------------|------|------------------------------------------------------|----------------|
| `monsys.9` | 1    | MONSYS «Дубна» — ОС БЭСМ-6, лента 011 oct            | user-provided  |
| `librar.12`| 2    | CERNlib lib1 (Common Software Library #1), 012 oct   | user-provided  |
| `librar.37`| 3    | CERNlib lib2 (Common Software Library #2), 037 oct   | user-provided  |
| `bemsh.739`| 4    | DISPAC/BEMSH — командный процессор БЭСМ-6, 0331 oct  | user-provided  |
| `b.7`      | 5    | Компилятор B (FORTRAN) для БЭСМ-6, 007 oct           | user-provided  |

## Юридический статус

Образы — историческое ПО БЭСМ-6 (1960–70-е, Дубна/CERN). В **этом репозитории** они
**git-ignored** (`.gitignore`: строки `ref/`, `tapes/`) и **намеренно не коммитятся** и не
встраиваются в `dotnet publish`. Право на переразрешение (распространение) конкретным образом
подтверждено не было, поэтому каждый образ помечен `RuntimeAssetLicense.UserProvided`:
пользователь предоставляет его сам, в пределах лицензии своего учреждения.

> Если вы получили право на распространение образа — см. раздел «Включение в пакет» ниже.

## Установка (documented installer step)

1. Положите каждый образ в каталог `src/besm6.net/tapes/` (или в любой каталог).
2. Либо укажите каталог в конфиге `besm6.json`: `{ "tapes": "<абсолютный-путь>/tapes" }`,
   либо через переменную окружения `BESM6_PATH` (каталог с лентами).

Порядок поиска каталогов (`RuntimeAssets.SearchDirectories`):
1. явно указанный абсолютный путь (config `tapes`);
2. каталог конфигурации (относительно `besm6.json`, `Config.ResolvePath`);
3. каталог приложения (`AppContext.BaseDirectory` — куда кладёт `dotnet publish`);
4. dev-поиск вверх (`tapes/`, `ref/tapes/`, `ref/dubna/tapes/`) — только для developer checkout.

## Проверка integrity (SHA256)

`RuntimeAssets.Resolve`/`ResolveInDirs` проверяет каждый найденный образ по SHA256 из каталога
и бросает `RuntimeAssetsException` (fail-fast) при отсутствии файла или несовпадении суммы.
Ожидаемые SHA256 (нижний регистр):

- `monsys.9`  `cc27c8d982231442e4d5b2bb6672945cbcd8caaf47ff3be1e578c5de621908ec`
- `librar.12` `4fbfb41bfac01949eafa084fb35fd915c211ed16f9417694b107bc0f23f0bb14`
- `librar.37` `0575e9bba22a87a1d59de4a2586d698d6fbb0bc3cff0a6d1db7a63428c0f0bc7`
- `bemsh.739` `69458c72286e9fe8ed3bc1d448ed20754d59259bfb5d7a7484850446481d0850`
- `b.7`       `7d6d864a103f309b5adca2a46abacffbc3d226aadd7c4a372c4fcea912c33f80`

Можно сверить самостоятельно:
```powershell
Get-FileHash -Algorithm SHA256 src/besm6.net/tapes/*.9, src/besm6.net/tapes/*.12,
  src/besm6.net/tapes/*.37, src/besm6.net/tapes/*.739, src/besm6.net/tapes/*.7
```

## Fail-fast

`besm6 run <job.dub>` проверяет runtime-ресурсы **до** запуска процессора
(`MachineFactory.ValidateRuntimeAssets`). При отсутствии/порче образа команда возвращает ненулевой
код и перечисляет: каждый отсутствующий/неверный ресурс, его происхождение, ожидаемую SHA256,
способ получения и все проверенные каталоги. Это устраняет «скрытую» зависимость от `ref/dubna`.

## Включение в пакет (если есть право на распространение)

Если право на переразрешение подтверждено, добавьте в `src/besm6.net/besm6.csproj` (условно,
чтобы чистый checkout без лент продолжал собираться):

```xml
<ItemGroup>
  <None Include="tapes\monsys.9"  Condition="Exists('tapes\monsys.9')"  CopyToPublishDirectory="PreserveNewest" />
  <None Include="tapes\librar.12" Condition="Exists('tapes\librar.12')" CopyToPublishDirectory="PreserveNewest" />
  <None Include="tapes\librar.37" Condition="Exists('tapes\librar.37')" CopyToPublishDirectory="PreserveNewest" />
  <None Include="tapes\bemsh.739" Condition="Exists('tapes\bemsh.739')" CopyToPublishDirectory="PreserveNewest" />
  <None Include="tapes\b.7"       Condition="Exists('tapes\b.7')"       CopyToPublishDirectory="PreserveNewest" />
</ItemGroup>
```

и пометьте соответствующие образы `RuntimeAssetLicense.Bundled`.
