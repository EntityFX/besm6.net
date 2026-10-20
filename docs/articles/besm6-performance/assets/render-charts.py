"""Rebuild article figures with matplotlib: python assets/render-charts.py.

Ratings come from benchmark-results.md. Section times come from the two
Whetstone listings reproduced in real-article.md; no new runs are performed.
"""

from pathlib import Path

import matplotlib

matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np


ASSETS = Path(__file__).resolve().parent
ARTICLE = ASSETS.parent
COLORS = ("#167d9a", "#de8744")
plt.rcParams.update({
    "font.family": "DejaVu Sans",
    "font.size": 11,
    "axes.spines.top": False,
    "axes.spines.right": False,
    "axes.spines.left": False,
    "axes.edgecolor": "#cbd5df",
    "text.color": "#203344",
    "axes.labelcolor": "#405566",
    "xtick.color": "#405566",
    "ytick.color": "#405566",
    "savefig.facecolor": "white",
})


def number(value):
    return f"{value:.3f}".replace(".", ",")


rows = []
for line in (ARTICLE / "benchmark-results.md").read_text(encoding="utf-8").splitlines():
    cells = [cell.strip() for cell in line.strip("|").split("|")]
    if len(cells) == 6 and cells[0].startswith(("Dhrystone", "Whetstone", "Memspeed", "MP-MFLOPS")):
        rows.append((cells[0], float(cells[2]), float(cells[4])))
assert len(rows) == 8

fig, axes = plt.subplots(2, 2, figsize=(13, 8.2))
groups = [
    ("Dhrystone 2.1", "DMIPS", rows[:1], ["5000 проходов"]),
    ("Whetstone", "MWIPS", rows[1:2], ["8 секций"]),
    ("Memspeed", "MB/s", rows[2:5], ["Копирование", "Сложение", "Умножение + сложение"]),
    ("MP-MFLOPS", "MFLOPS", rows[5:], ["2 операции", "8 операций", "32 операции"]),
]
for ax, (title, unit, data, labels) in zip(axes.flat, groups):
    y = np.arange(len(data))
    maximum = max(row[2] for row in data)
    for index, (offset, color, label) in enumerate(zip(
        (-0.18, 0.18), COLORS, ("original — время модели", "virtual — часы хоста")
    )):
        values = [row[index + 1] for row in data]
        bars = ax.barh(y + offset, values, height=0.31, color=color, label=label, zorder=3)
        ax.bar_label(bars, labels=[number(v) for v in values], padding=5, fontsize=10)
    ax.set_title(title, loc="left", fontweight="bold", pad=14)
    ax.set_yticks(y, labels)
    ax.invert_yaxis()
    ax.set_xlim(0, maximum * 1.3)
    ax.set_xlabel(unit, loc="right")
    ax.tick_params(axis="y", length=0, pad=8)
    ax.grid(axis="x", color="#e6ebf0", zorder=0)
    ax.margins(y=0.3)
handles, labels = axes[0, 0].get_legend_handles_labels()
fig.legend(handles, labels, loc="upper center", bbox_to_anchor=(0.5, 0.94), ncol=2, frameon=False)
fig.suptitle("БЭСМ-6: модель и скорость симулятора", x=0.06, ha="left", fontsize=20, fontweight="bold")
fig.text(0.06, 0.015, "Медианы трёх запусков. У каждой панели собственная шкала; больше — быстрее.", fontsize=10)
fig.subplots_adjust(left=0.19, right=0.96, top=0.82, bottom=0.1, hspace=0.47, wspace=0.65)
fig.savefig(ASSETS / "benchmark-ratings.png", dpi=160)
plt.close(fig)

historical_model = [0.293, 2.674, 4.477, 7.280, 18.361, 35.720, 13.963, 16.087]
besm6_net = [0.190, 1.720, 3.200, 3.860, 18.120, 36.960, 18.460, 16.340]
labels = ["N1 · Арифметика", "N2 · Арифметика", "N3 · Условия", "N4 · Целые числа",
          "N5 · sin, cos…", "N6 · Арифметика", "N7 · Присваивания", "N8 · exp, sqrt…"]
fig, ax = plt.subplots(figsize=(12, 7))
y = np.arange(8)
for values, offset, color, label in zip(
    (historical_model, besm6_net), (-0.18, 0.18), COLORS,
    ("Модель Leo B., 2017", "Besm6.Net, original"),
):
    bars = ax.barh(y + offset, values, height=0.31, color=color, label=label, zorder=3)
    ax.bar_label(bars, labels=[number(v) for v in values], padding=5, fontsize=10)
ax.set_yticks(y, labels)
ax.invert_yaxis()
ax.set_xlim(0, 43)
ax.tick_params(axis="y", length=0, pad=10)
ax.grid(axis="x", color="#e6ebf0", zorder=0)
ax.set_xlabel("Нормализованное время секции, с", loc="right")
ax.legend(loc="lower right", frameon=False)
fig.suptitle("Whetstone: одинаковый итог, разные времена секций", x=0.05, ha="left", fontsize=18, fontweight="bold")
fig.text(0.05, 0.025, "Обе серии относятся к моделям БЭСМ-6. Сумма приведённых времён: 98,855 и 98,850 с.", fontsize=10)
fig.subplots_adjust(left=0.2, right=0.96, top=0.9, bottom=0.13)
fig.savefig(ASSETS / "whetstone-sections.png", dpi=160)
plt.close(fig)
