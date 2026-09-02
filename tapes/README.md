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
в `src/besm6.net/Loader/RuntimeAssets.cs` и `docs/runtime-assets.md`.

Не переименовывайте и не преобразовывайте образы: build/publish копирует их как есть,
а симулятор проверяет SHA256 до начала исполнения.
