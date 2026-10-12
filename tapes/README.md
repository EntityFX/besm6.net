# Bundled BESM-6 tape images

Эти пять образов требуются для MONSYS, CERNlib и BEMSH workloads:

- `monsys.9` — MS Dubna installation tape;
- `librar.12` — Common Software Library #1;
- `librar.37` — Common Software Library #2;
- `bemsh.739` — DISPAC/BEMSH;
- `b.7` — B/FORTRAN compiler tape.

Файлы импортированы из MIT-проекта `dubna`, revision
`ee2a098a69cd808c25e2e42205ab9f61a3372850`. Копия лицензии находится в
`ref/LICENSE`, provenance и состав корпуса — в `ref/README.md`, контрольные суммы —
в `src/Besm6.Runtime/Loading/RuntimeAssets.cs` и `docs/runtime-assets.md`.

`monsys.9` восстановлен побитово по `tapes/monsys9.c` из upstream
`besm6/dubna`, commit `e8c7f78b612284e712bf541ba96cfbde9dd49bb2`.
От прежнего bundled-образа отличаются только 18 байт `0x8FFEE–0x8FFFF`:
три машинных слова были заполнены `0x20`, что ломало трансляцию присваиваний
в ФОРЕКС. Остальные четыре ленты совпадают с этим upstream побитово.

Не переименовывайте и не преобразовывайте образы: build/publish копирует их как есть,
а симулятор проверяет SHA256 до начала исполнения.
