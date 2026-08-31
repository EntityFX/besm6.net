# Golden-выводы fast-примеров (Task A5, инкремент 2)

Эти файлы — **утверждённый эталонный вывод** трёх fast-примеров. Acceptance-гейт
(`tools/run_all_examples.py`) считает пример успешным только если его stdout совпадает
с соответствующим `*.txt` (нормализованное сравнение: CRLF/LF + отбел пробелов/пустых
строк). Без golden-файла пример проверяется по exit code + StopReason.

## Детерминированность

Выводы сгенерированы с флагом `--no-wall-clock` (его принудительно передаёт и гейт).
Иначе баннер MONSYS содержит текущее время (`АПР 26 16:48`), и golden не совпадает
между прогонами. Это обязателно по глобальному ограничению плана: детерминированные
тесты не используют реальные часы.

## Перегенерация

Если вывод примеров законно изменился (исправление в симуляторе), перегенерируйте:

```powershell
dotnet build src/besm6.net/besm6.net.sln
python tools/run_all_examples.py --suite fast   # проверяет, что текущий вывод совпадает с golden
```

И при необходимости обновите файлы вручную из `tests-run/examples/scenarios/*.out`
(чистый stdout каждого сценария), затем перезапустите гейт и Python-тесты.

## Проверка

```powershell
python -m unittest discover -s tools/tests -p "test_*.py"   # вкл. GoldenFilesTests
python tools/run_all_examples.py --suite fast               # 3/3 OK против этих golden
```