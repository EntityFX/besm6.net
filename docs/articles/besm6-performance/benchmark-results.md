# Результаты измерений Besm6.Net

Коммит `190e2f6977190e42f34b33d2e56424454be89027`. По три последовательных запуска; времена и рейтинги — медианы. Original — калиброванная статистическая модель, virtual — часы хоста. Whetstone использует нормализованную сумму секций.

| Бенчмарк | Время original, с | Рейтинг original | Время virtual, с | Рейтинг virtual | Единица |
|---|---:|---:|---:|---:|---|
| Dhrystone 2.1 | 4.360 | 0.653 | 0.340 | 8.370 | DMIPS |
| Whetstone | 98.850 | 0.809 | 4.974 | 16.084 | MWIPS |
| Memspeed: copy | 8.680 | 5.663 | 0.960 | 51.200 | MB/S |
| Memspeed: add | 16.740 | 4.404 | 1.220 | 60.433 | MB/S |
| Memspeed: multiply-add | 17.640 | 4.180 | 1.480 | 49.816 | MB/S |
| MP-MFLOPS: 2 ops | 1.100 | 0.491 | 0.080 | 6.750 | MFLOPS |
| MP-MFLOPS: 8 ops | 3.340 | 0.647 | 0.240 | 9.000 | MFLOPS |
| MP-MFLOPS: 32 ops | 13.540 | 0.638 | 0.820 | 10.537 | MFLOPS |

| Бенчмарк | Режим | Число прогонов | Медиана | Минимум | Максимум | Единица |
|---|---|---:|---:|---:|---:|---|
| Dhrystone 2.1 | original | 3 | 0.652697 | 0.652697 | 0.652697 | DMIPS |
| Dhrystone 2.1 | virtual | 3 | 8.369882 | 8.369882 | 8.369882 | DMIPS |
| Whetstone | original | 3 | 0.809307 | 0.809307 | 0.809307 | MWIPS |
| Whetstone | virtual | 3 | 16.083635 | 16.083635 | 16.474465 | MWIPS |
| Memspeed: copy | original | 3 | 5.662673 | 5.662673 | 5.662673 | MB/S |
| Memspeed: copy | virtual | 3 | 51.200000 | 39.638710 | 54.613333 | MB/S |
| Memspeed: add | original | 3 | 4.404301 | 4.404301 | 4.404301 | MB/S |
| Memspeed: add | virtual | 3 | 60.432787 | 49.816216 | 62.481356 | MB/S |
| Memspeed: multiply-add | original | 3 | 4.179592 | 4.179592 | 4.179592 | MB/S |
| Memspeed: multiply-add | virtual | 3 | 49.816216 | 39.638710 | 51.200000 | MB/S |
| MP-MFLOPS: 2 ops | original | 3 | 0.490909 | 0.490909 | 0.490909 | MFLOPS |
| MP-MFLOPS: 2 ops | virtual | 3 | 6.750000 | 5.400000 | 6.750000 | MFLOPS |
| MP-MFLOPS: 8 ops | original | 3 | 0.646707 | 0.646707 | 0.646707 | MFLOPS |
| MP-MFLOPS: 8 ops | virtual | 3 | 9.000000 | 9.000000 | 9.818182 | MFLOPS |
| MP-MFLOPS: 32 ops | original | 3 | 0.638109 | 0.638109 | 0.638109 | MFLOPS |
| MP-MFLOPS: 32 ops | virtual | 3 | 10.536585 | 10.536585 | 11.076923 | MFLOPS |

Сводные результаты опубликованы в таблицах выше. Файлы отдельных прогонов и полный манифест окружения в комплект статьи не включены.
