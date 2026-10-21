#!/usr/bin/env python3
"""Build the article tables and figures from validated measurement evidence."""
import json
import statistics
from pathlib import Path

import matplotlib
matplotlib.use('Agg')
import matplotlib.pyplot as plt
from matplotlib.patches import Rectangle

HERE = Path(__file__).resolve().parent
DATA = HERE / 'data/run-2026-10-13'
ASSETS = HERE / 'assets'
LABELS = {('dhrystone', 0): 'Dhrystone 2.1', ('whetstone', 0): 'Whetstone',
          ('memspeed', 1): 'Memspeed: copy', ('memspeed', 2): 'Memspeed: add',
          ('memspeed', 3): 'Memspeed: multiply-add', ('mpmflops', 1): 'MP-MFLOPS: 2 ops',
          ('mpmflops', 2): 'MP-MFLOPS: 8 ops', ('mpmflops', 3): 'MP-MFLOPS: 32 ops'}
COLORS = ['#2458a6', '#c76524']


def save(fig, name):
    fig.savefig(ASSETS / (name + '.png'), dpi=180, bbox_inches='tight', facecolor='white')
    fig.savefig(ASSETS / (name + '.svg'), bbox_inches='tight', facecolor='white')
    plt.close(fig)


def main():
    rows = json.loads((DATA / 'results.json').read_text())
    summaries = json.loads((DATA / 'summary.json').read_text())
    if len(rows) != 48 or not all(r['validated'] for r in rows):
        raise ValueError('Expected 48 validated measurements')
    ASSETS.mkdir(exist_ok=True)
    index = {(r['benchmark'], r['part'], r['mode']): r for r in summaries}
    table = '| Бенчмарк | Время original, с | Рейтинг original | Время virtual, с | Рейтинг virtual | Единица |\n|---|---:|---:|---:|---:|---|\n'
    for key, label in LABELS.items():
        a, b = [index[(*key, mode)] for mode in ['original', 'virtual']]
        table += f'| {label} | {a["seconds_median"]:.3f} | {a["rating_median"]:.3f} | {b["seconds_median"]:.3f} | {b["rating_median"]:.3f} | {a["unit"]} |\n'
    detail = '| Бенчмарк | Режим | Число прогонов | Медиана | Минимум | Максимум | Единица |\n|---|---|---:|---:|---:|---:|---|\n'
    for r in summaries:
        detail += f'| {LABELS[(r["benchmark"],r["part"])]} | {r["mode"]} | {r["repeats"]} | {r["rating_median"]:.6f} | {r["rating_min"]:.6f} | {r["rating_max"]:.6f} | {r["unit"]} |\n'
    (HERE / 'benchmark-results.md').write_text('# Результаты измерений Besm6.Net\n\nКоммит `190e2f6977190e42f34b33d2e56424454be89027`. По три последовательных запуска; времена и рейтинги — медианы. Original — калиброванная статистическая модель, virtual — часы хоста. Whetstone использует нормализованную сумму секций.\n\n' + table + '\n' + detail + '\n[CSV](data/run-2026-10-13/results.csv), [JSON и полная статистика](data/run-2026-10-13/results.json), [окружение](data/run-2026-10-13/environment.json).\n')
    fig, axes = plt.subplots(2, 2, figsize=(12, 7.4), layout='constrained')
    for ax, family, unit in zip(axes.flat, ['dhrystone', 'whetstone', 'memspeed', 'mpmflops'], ['DMIPS', 'MWIPS', 'MB/s', 'MFLOPS']):
        keys = [key for key in LABELS if key[0] == family]
        labels = [LABELS[k].split(': ')[-1] for k in keys]
        for j, mode in enumerate(['original', 'virtual']):
            rr = [index[(*k, mode)] for k in keys]
            med = [r['rating_median'] for r in rr]
            xs = [i + (-.18 if j == 0 else .18) for i in range(len(keys))]
            err = [[r['rating_median']-r['rating_min'] for r in rr], [r['rating_max']-r['rating_median'] for r in rr]]
            bars = ax.bar(xs, med, width=.34, color=COLORS[j], label='original: модель' if j == 0 else 'virtual: хост', yerr=err, capsize=3)
            ax.bar_label(bars, fmt='%.2f', fontsize=9, padding=4)
        ax.set_xticks(range(len(keys)), labels, fontsize=9)
        ax.set_ylabel(unit)
        ax.set_title(family.upper() if family == 'mpmflops' else family.capitalize())
        ax.set_ylim(0, ax.get_ylim()[1]*1.15)
        ax.grid(axis='y', alpha=.2)
        ax.set_axisbelow(True)
    axes[0, 0].legend(fontsize=9)
    fig.suptitle('Скорость хоста и рейтинг модели: разные источники времени\nМедианы 3 прогонов; черточки — min–max, разные единицы на панелях', fontsize=14)
    save(fig, 'benchmark-ratings')

    w = [r for r in rows if r['benchmark'] == 'whetstone' and r['mode'] == 'original']
    secs = [statistics.median(r['extra']['sections'][i]['seconds'] for r in w) for i in range(8)]
    published = [.293, 2.674, 4.477, 7.280, 18.361, 35.720, 13.963, 16.087]
    fig, ax = plt.subplots(figsize=(11, 5), layout='constrained')
    ax.bar([i-.18 for i in range(8)], published, .34, label='Публикация 2017: другая модель', color='#747c87')
    ax.bar([i+.18 for i in range(8)], secs, .34, label='Besm6.Net original: после калибровки', color=COLORS[0])
    ax.set_xticks(range(8), ['N'+str(i) for i in range(1, 9)])
    ax.set_ylabel('Нормализованное время секции, с')
    ax.set_title('Совпадение общего MWIPS не означает совпадения секций')
    ax.legend()
    ax.grid(axis='y', alpha=.2); ax.set_axisbelow(True)
    fig.supxlabel('Оба набора — модели. Данные 2017 использованы для фита; это не независимая валидация.', fontsize=10)
    save(fig, 'whetstone-sections')

    fig, ax = plt.subplots(figsize=(12, 5.8), layout='constrained')
    ax.set_xlim(-1, 49); ax.set_ylim(-.9, 5.4); ax.axis('off')
    def fields(y, widths, labels, colors):
        start = 0
        for width, label, color in zip(widths, labels, colors):
            ax.add_patch(Rectangle((start, y), width, .7, facecolor=color, edgecolor='white', linewidth=2))
            ax.text(start+width/2, y+.35, label, ha='center', va='center', fontsize=11, color='white')
            start += width
    ax.text(0, 5.0, '48 полезных разрядов БЭСМ-6', fontsize=20, weight='bold')
    ax.text(0, 4.55, 'Разряды книги нумеруются справа налево от 1 до 48. Два контрольных разряда не показаны.', fontsize=10)
    fields(3.55, [24,24], ['Левая команда: 48–25', 'Правая команда: 24–1'], ['#2458a6','#4c80c7'])
    fields(2.25, [7,1,40], ['Порядок\n7 бит', '', 'Дробная часть мантиссы: 40 бит'], ['#247a65','#b94638','#4c80c7'])
    ax.annotate('Знак: разряд 41', xy=(7.5,2.6), xytext=(11,3.17), fontsize=10, arrowprops={'arrowstyle':'->','color':'#555'})
    fields(.95, [48], ['Логическое слово: маска, символы, произвольный код'], ['#555f70'])
    ax.text(0, .23, 'Короткая команда: индекс 4 + признак 1 + расширение адреса 1 + операция 6 + адрес 12 = 24', fontsize=11)
    ax.text(0, -.16, 'Длинная команда: индекс 4 + признак 1 + операция 4 + адрес 15 = 24', fontsize=11)
    ax.text(0, -.58, 'REAL здесь не IEEE 754. Представление INTEGER задаёт транслятор.', fontsize=10, color='#555')
    save(fig, 'word48')

    gm_model = statistics.median(r['extra']['mflops_geomean'] for r in w)
    gm_host = statistics.median(r['extra']['mflops_geomean'] for r in rows if r['benchmark']=='whetstone' and r['mode']=='virtual')
    section_table = '| Секция | Публикация 2017, с | Наша модель original, с |\n|---|---:|---:|\n'
    for i, (a, b) in enumerate(zip(published, secs), 1):
        section_table += f'| N{i} | {a:.3f} | {b:.3f} |\n'
    text = '### Результаты\n\nВсе 48 запусков завершились и прошли описанные проверки. Таблица показывает медианы трёх запусков для каждой пары задания и режима. Время Whetstone — сумма нормализованных секций; у остальных тестов — интервал ядра.\n\n' + table
    text += f'\nДля Whetstone отдельное геометрическое среднее арифметических секций составило **{gm_model:.3f} MFLOPS** в модели и **{gm_host:.3f} MFLOPS** по часам хоста. Это отдельный показатель, а не перевод MWIPS в другие единицы.\n\n'
    text += '![Рейтинги модели и симулятора на хосте](assets/benchmark-ratings.png)\n\nУ диаграммы четыре шкалы: сравнивать высоты столбцов между панелями нельзя. Полный разброс и времена каждого прогона сохранены в [отдельной таблице](benchmark-results.md).\n\n'
    text += 'В Dhrystone рейтинг по часам хоста выше модельного: симулятор исполняет ядро быстрее, чем время, назначенное моделью. Это различие относится к данному порту и хосту. Оно не является универсальным коэффициентом, на который можно делить любую современную программу ради оценки БЭСМ-6.\n\n'
    text += 'Три результата Memspeed включают управление циклами и арифметику адресов. Поэтому столбец copy — скорость всего гостевого ядра копирования. У MP-MFLOPS изменение числа операций на элемент меняет долю служебной работы; общий MFLOPS также зависит от сгенерированных команд и зависимостей между ними. Ни одна из этих строк не задаёт единую «скорость всей машины».\n\n'
    text += 'У самого короткого ядра MP-MFLOPS интервал `virtual` составил 0,08–0,10 с. Один квант 20 мс здесь сравним с четвертью времени: несколько десятичных знаков рейтинга не означают соответствующую точность. У Memspeed разброс также заметен. Эти результаты полезны для повторения данного опыта, но строгий анализ скорости хоста потребовал бы более длинных интервалов и контроля условий измерения. Точное повторение модельного времени в `original` показывает детерминированность модели; оно не определяет её ошибку относительно исторической машины.\n\n'
    text += '### Совпавший Whetstone и несовпавшие секции\n\nКалиброванный общий MWIPS близок к значению, по которому настраивали модель. Секционные времена при этом различаются:\n\n' + section_table
    text += '\n![Секции двух моделей Whetstone](assets/whetstone-sections.png)\n\nЗдесь обе серии относятся к моделям. Версии транслятора, системное окружение и численные значения исходного эксперимента отличаются. Такую диаграмму следует читать как проверку последствий калибровки, а не как измеренную ошибку относительно физической БЭСМ-6.\n\n'
    text += '### Накладные расходы симуляции\n\nВ `virtual` в интервал ядра попадают интерпретация команд, работа модели памяти и обслуживающий код хоста. Компиляция гостевой программы остаётся за пределами этого интервала, но присутствует в полном времени запуска. Сборка Release и данные окружения позволяют повторить замер; они не позволяют отдельно вычесть стоимость каждого внутреннего механизма.\n\nВ `original` к этому добавляется ожидание назначенного темпа. Поэтому полное время процесса показывает длительность нашего опыта, а гостевое модельное время — расчёт модели. Простой пересчёт «host seconds × частота БЭСМ-6» не восстанавливает историческое время: он игнорирует зависимости, память, стоимость экстракодов и различие между компиляцией и ядром.\n'
    (HERE / 'measured-section.md').write_text(text)
    print(f'Generated tables and 3 figures; Whetstone model {gm_model:.6f} MFLOPS')


if __name__ == '__main__':
    main()
