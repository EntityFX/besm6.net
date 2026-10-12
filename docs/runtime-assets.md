# Runtime-ресурсы (ленты БЭСМ-6): поставка, проверка, установка

> SuperPlan Task A4 — runtime-ресурсы как явная часть поставки, без скрытой зависимости от
> developer junction `ref/dubna`.

## Какие образы нужны

Исполнение MONSYS/CERN-нагрузки требует пять лент. Каталог (checksum-manifest) задаётся в
`src/besm6.net/Loader/RuntimeAssets.cs` (`Besm6.Loader.RuntimeAssets.Catalog`) и включает имя,
тап-узел, ожидаемый SHA256, происхождение, класс лицензии и способ получения.

| Файл       | Узел | Происхождение                                        | Лицензия       |
|------------|------|------------------------------------------------------|----------------|
| `monsys.9` | 1    | MONSYS «Дубна» — ОС БЭСМ-6, лента 011 oct            | bundled (MIT)  |
| `librar.12`| 2    | CERNlib lib1 (Common Software Library #1), 012 oct   | bundled (MIT)  |
| `librar.37`| 3    | CERNlib lib2 (Common Software Library #2), 037 oct   | bundled (MIT)  |
| `bemsh.739`| 4    | DISPAC/BEMSH — командный процессор БЭСМ-6, 0331 oct  | bundled (MIT)  |
| `b.7`      | 5    | Компилятор B (FORTRAN) для БЭСМ-6, 007 oct           | bundled (MIT)  |

## Юридический статус

Образы и тестовый corpus импортированы из проекта `dubna` revision
`ee2a098a69cd808c25e2e42205ab9f61a3372850`, распространяемого под MIT License.
Копия лицензии и точный состав импорта находятся в `ref/LICENSE` и `ref/README.md`.
Каждый обязательный образ помечен `RuntimeAssetLicense.Bundled`, коммитится в `tapes/`
и включается в build/publish output.

## Установка (documented installer step)

Обычная установка не требует ручных действий: `dotnet publish` кладёт образы в
`tapes/` рядом с приложением. Для проверки другого набора образов можно указать каталог
в `besm6.json`: `{ "tapes": "<абсолютный-путь>/tapes" }` либо через `BESM6_PATH`.

Порядок поиска каталогов (`RuntimeAssets.SearchDirectories`):
1. явно указанный абсолютный путь (config `tapes`);
2. каталог конфигурации (относительно `besm6.json`, `Config.ResolvePath`);
3. каталог приложения (`AppContext.BaseDirectory` — куда кладёт `dotnet publish`);
4. dev-поиск вверх (`tapes/`, `ref/tapes/`, `ref/dubna/tapes/`) — только для developer checkout.

## Восстановление кода ФОРЕКС в MONSYS

В исходном bundled `monsys.9` 18 байт `0x8FFEE–0x8FFFF` были равны
`0x20` (ASCII-пробел). Это три слова в конце зоны 95 (137 oct),
восстанавливаемые по `tapes/monsys9.c` проекта `besm6/dubna`, commit
`e8c7f78b612284e712bf541ba96cfbde9dd49bb2`. Остальные байты MONSYS и все
четыре другие bundled-ленты совпадают с этим upstream.

Каждое испорченное слово `0x202020202020` декодируется как
`mod 40(2),mod 40(2)` — это не намеренная ловушка компилятора.
При трансляции `X=1.0` слова загружались в адреса `40134–40136` (oct),
и исполнение падало на `40134L` (`0405C` hex).
Правильные слова содержат:

```text
3411003334120034  aax 33(7),aex 34(7)
3667552573142256  uza 75525(7),vjm 42256(16)
3410003044150000  xta 30(7),aox (11)
```

Восстановление этих 18 байт устраняет сбой присваиваний и позволяет
выполнять Whetstone настоящим ФОРЕКС 4.13 без изменения семантики `002`.
Проверка арифметики сохранена в `ForexCompilerTests`; прежний SHA256
`cc27c8d982231442e4d5b2bb6672945cbcd8caaf47ff3be1e578c5de621908ec`
относится к испорченному образу.

## Проверка integrity (SHA256)

`RuntimeAssets.Resolve`/`ResolveInDirs` проверяет каждый найденный образ по SHA256 из каталога
и бросает `RuntimeAssetsException` (fail-fast) при отсутствии файла или несовпадении суммы.
Ожидаемые SHA256 (нижний регистр):

- `monsys.9`  `e26a2036011bc33a2671b052ea63eaa60b6f577235cfe7080837978d1df1118b`
- `librar.12` `4fbfb41bfac01949eafa084fb35fd915c211ed16f9417694b107bc0f23f0bb14`
- `librar.37` `0575e9bba22a87a1d59de4a2586d698d6fbb0bc3cff0a6d1db7a63428c0f0bc7`
- `bemsh.739` `69458c72286e9fe8ed3bc1d448ed20754d59259bfb5d7a7484850446481d0850`
- `b.7`       `7d6d864a103f309b5adca2a46abacffbc3d226aadd7c4a372c4fcea912c33f80`

Можно сверить самостоятельно:
```powershell
Get-FileHash -Algorithm SHA256 tapes/*.9, tapes/*.12, tapes/*.37, tapes/*.739, tapes/*.7
```

## Fail-fast

`besm6 run <job.dub>` проверяет runtime-ресурсы **до** запуска процессора
(`MachineFactory.ValidateRuntimeAssets`). При отсутствии/порче образа команда возвращает ненулевой
код и перечисляет: каждый отсутствующий/неверный ресурс, его происхождение, ожидаемую SHA256,
способ получения и все проверенные каталоги. Это устраняет «скрытую» зависимость от `ref/dubna`.

## Поставка

`src/besm6.net/besm6.csproj` включает `tapes/**` с
`CopyToOutputDirectory=PreserveNewest` и `CopyToPublishDirectory=PreserveNewest`.
Тест `RuntimeAssetsTests.Catalog_Sha256_MatchesShippedImages` защищает package corpus
от случайной замены или повреждения.
