# MosaicGenerator

Генератор макетов (картонов) мозаики из смальты. Пользователь загружает фотографию,
задаёт размеры панно и уровень детализации — получает превью раскладки, рабочую схему
(пронумерованные тессеры) и таблицу расхода материала по артикулам.

Инструмент для себя: один мастер-мозаичист, новичок в смальте. Не продукт для рынка,
не многопользовательское приложение — оптимизируем под один рабочий процесс, а не под гибкость.

## Ключевые документы

- `TODO.md` — единый бэклог с аргументацией: почему это проблема, чем подтверждается,
  где лежит в коде. Держать задачи в двух местах нельзя.
- `docs/*.md` — планы исследовательских изменений и диагностические разборы.

## Стек и структура

- **C# / .NET 9**, `Nullable` и `ImplicitUsings` включены (`Directory.Build.props`).
- **ASP.NET Core MVC** (не Web API, не SPA): один контроллер, несколько Razor-вьюх,
  немного ванильного JS для кропа. Без БД, без фоновых задач — всё синхронно.

| Проект | Назначение |
|---|---|
| `src/MosaicGenerator.Core` | Ядро: цветовые пространства, поле направлений, квантование, геометрия рендера, оптика шва, расчёт материала |
| `src/MosaicGenerator.Web` | ASP.NET Core MVC: контроллер, вьюхи, хранилища во временных папках |
| `tests/MosaicGenerator.Core.Tests` | xUnit по ядру (сотни тестов, покрывают раскладку без сравнения картинок) |
| `tools/MosaicGenerator.Diag` | Диагностический стенд: прогон фото по матрице параметров, `metrics.csv`, превью/схемы |

## Команды

```bash
dotnet test                                    # тесты ядра
dotnet run --project src/MosaicGenerator.Web   # http://localhost:5199

# Диагностический прогон одной фотографии
dotnet run -c Release --project tools/MosaicGenerator.Diag -- \
    --photo samples/gull.jpg --out diag-out/gull \
    --sizes 21x21,21x29.7,40x40 --modules 10 --colors 12 --gamut

# --why добавляет вскрытие укладчика: откуда посеян каждый курс, обо что оборвался,
# сколько кусков дал при каком откусе, что делает шаг 5, вершины и зазоры по роли куска
```

Картоны из прогона лежат в `--out` как `<размер>-cartoon.png` — **смотреть их глазами**,
а не только `metrics.csv` (см. «Судья — картон, а не метрика»).

## Предметная область

Терминология и вся документация — **на русском**. Ключевые понятия:

- **Тессера / модуль** — отдельный кусок стекла
- **Шов** — щель между тессерами. Смальта на белом клею, **не затирается**; цвет шва —
  затенённая постель клея, считается аналитически (`Core/Optics/JointAppearance.cs`).
- **Курс** — ряд тессер. Укладка идёт курсами по форме (лёгкое андаменто): поле
  направлений из структурного тензора, линии тока Jobard–Lefèvre, opus vermiculatum
  вокруг силуэта, opus musivum в фоне.
- **Панно** — готовая работа. **Поле мозаики** — панно минус поля по периметру.
- **Схема / картон** — пронумерованная раскладка для набора вручную.
- **Артикул** — единственный достоверный идентификатор цвета смальты. Палитра —
  152 артикула ArtWorker, HEX сняты с фотографии физического веера образцов.

## Рабочие ориентиры

- Приоритеты расставлены по размерам **21×21, 40×40 см, A4 (21×29.7)**, **10–15 цветов**.
  60×60 держим как эталон и снимаем на воротных кадрах. 15×15 из приёмки убран 2026-09-05:
  13 тессер поперёк — нечитаемо для почти любого сюжета, это пункт 10 бэклога, а не укладка.
- Замеры с натуры: кусачки/молоток, куски 6–12 мм, швы 1–2 мм. Порог узкой стороны
  куска 5 мм.
- **Все цифры приёмки меряются стендом `tools/MosaicGenerator.Diag` по всем фото
  из `samples/`**, не по трём: настройки однажды оказались подогнаны под `gull.jpg`.
  Полная процедура — две руки, ворота глазами, что не должно уехать — в `TODO.md`,
  раздел «Приёмка». Держать её в двух местах нельзя: сюда только ссылка.
- Прежде чем ставить цель по метрике — измерить её текущее дно (например, «merged 0,39»
  — это дно от 12 оттенков, а не дефект).

## Судья — картон, а не метрика

Цель проекта — раскладка, похожая на работу мастера. Метрика существует, чтобы **объяснить
увиденное** и не обмануться на одном фото; она не заменяет взгляд и не является целью.

- **Смотреть картон глазами** до и после любой правки алгоритма: `Read` по PNG из
  `diag-out/`, сравнение до/после на одном кадре. Если картон стал хуже, а метрика лучше —
  права картина, метрику пересмотреть.
- **Ложные метрики бывают.** Проверено 2026-09-04: «курсов < 3 кусков» дефектом не является —
  ряд той же длины в миллиметрах, просто набранный более крупным куском, это нормальный
  приём. Прежде чем гнаться за числом, спросить: какой видимый на картоне дефект оно ловит?
  Если ответа нет — числу грош цена.
- Метрики, у которых видимый эквивалент **есть** и проверен: доля филлеров (рябь из затычек
  в фоне), доля четырёхугольников и вершин у куска (откусываемость руками), площадь широких
  зазоров (дыры), излом курса.
- Формулировать выводы на языке набора («фон рябит клиньями», «грудь птицы — крошево»),
  а не на языке колонок CSV.

## Думать как мозаичист

- **Спрашивать, как мастер делает это руками** — до того, как менять алгоритм. Фон кладут
  крупнее — но крупнее в обе стороны, а не палочкой: у нас поперечник прибит к толщине
  пластины, и «крупнее» вырождалось в 20×7 (3:1), а такой кусок не ложится по дуге курса и
  оставляет клинья. Вылечено ограничением пропорции куска — не длиннее ~1,4 поперечника
  (`docs/proportsiya-kuska.md`, разбор укладчика — `docs/kursy-obryvayutsya-plan.md`).
- **Физика материала — часть задачи.** Смальта колется через пластину, поперечник куска
  задан её толщиной; кусок жёсткий и по кривой не гнётся; узкую сторону меньше 5 мм не
  поставить. Любое «улучшение», нарушающее это, ошибочно, как бы ни двигались цифры.
- **Причинность проверять экспериментом, а не рассуждением.** 2026-09-04 две убедительно
  выглядевшие гипотезы оказались ложными: снятие ограничения `pass == 0` в шаге 5 ничего не
  дало, а «виноват разнобой откусов между соседними курсами» опровергнут прогоном с единым
  откусом. Порядок: правка — замер — откат; вывод только после замера, а не до него.

## Как работаем

- **Перед крупным/исследовательским изменением** — план в `docs/*.md` с аргументацией
  и способом перепроверки, затем ждём «поехали». Не начинать правку алгоритма с кода.
- Не вводить скрытый технический долг.
- Встретил баг, несогласованность, неверный паттерн или техдолг по ходу задачи —
  либо чини на месте, либо сразу добавляй короткий пункт в `TODO.md`.
- Не запускать `git commit` или `git add` ни при каких обстоятельствах, пока
  пользователь явно не попросит.

<!-- gitnexus:start -->
# GitNexus — Code Intelligence

This project is indexed by GitNexus as **mosaic-generator** (2552 symbols, 6748 relationships, 214 execution flows).

> Index stale? Run `node .gitnexus/run.cjs analyze --index-only` from the project root — it auto-selects an available runner. No `.gitnexus/run.cjs` yet? Bootstrap with `npx`, `bunx`, or `pnpm dlx` — e.g. `bunx gitnexus@latest analyze` (npm 11 npx crash; #1939).

## Always Do

- **MUST run impact before editing.** Use `impact({target: "symbolName", direction: "upstream"})` or `node .gitnexus/run.cjs impact "symbolName" --direction upstream --repo .`; report callers, processes, and risk. Never substitute grep for graph analysis.
- **MUST analyze graph changes before committing.** Use `detect_changes({scope: "all"})` (MCP) or `node .gitnexus/run.cjs detect-changes --scope all --repo .` (CLI fallback). `partial: true` or `truncated: true` is not a clean check — a zero means unseen, not unaffected; re-run it. For regression review: `detect_changes({scope: "compare", base_ref: "main"})` or `node .gitnexus/run.cjs detect-changes --scope compare --base-ref "main" --repo .`.
- MUST warn on HIGH/CRITICAL `risk` pre-edit; never use `riskSharedAxes` to waive a HIGH/CRITICAL `risk` warning. Compare File/symbol: MCP File omits axes; Graph-RAG expands File.
- **MUST treat `risk: UNKNOWN` as unresolved, not as low.** An empty caller set is not evidence the symbol is unused — it can also mean the callers are not resolvable by the index (plain-object property access, dynamic dispatch, cross-language calls). `impact` pairs `UNKNOWN` with a `riskNote` saying so. Confirm with a text search before treating the symbol as safe to change or delete; do not proceed on the strength of a zero.
- **MUST use `query({search_query: "concept"})` for concepts/flows, `context({name: "symbolName"})` for a named symbol, or `impact` for blast radius, on read-only callers, dependencies, imports, or execution flow.** Graph first; text search only for empty/`UNKNOWN`/literals.
- For security review, `explain({target: "fileOrSymbol"})` lists taint findings (source→sink flows; needs `analyze --pdg`).

## Never Do

- NEVER edit a function, class, or method before MCP/CLI impact analysis.
- NEVER ignore HIGH or CRITICAL risk warnings from impact analysis, and never read `UNKNOWN` as an all-clear — it means the walk could not answer, which is the one verdict that requires confirming by other means.
- NEVER rename symbols with find-and-replace — use `rename` which understands the call graph.
- NEVER commit before MCP/CLI graph change analysis.

## Resources

| Resource | Use for |
| --- | --- |
| `gitnexus://repo/mosaic-generator/context` | Codebase overview, check index freshness |
| `gitnexus://repo/mosaic-generator/clusters` | All functional areas |
| `gitnexus://repo/mosaic-generator/processes` | All execution flows |
| `gitnexus://repo/mosaic-generator/process/{name}` | Step-by-step execution trace |

## CLI

| Task | Read this skill file |
| --- | --- |
| Understand architecture / "How does X work?" | `.claude/skills/gitnexus-exploring/SKILL.md` |
| Blast radius / "What breaks if I change X?" | `.claude/skills/gitnexus-impact-analysis/SKILL.md` |
| Trace bugs / "Why is X failing?" | `.claude/skills/gitnexus-debugging/SKILL.md` |
| Rename / extract / split / refactor | `.claude/skills/gitnexus-refactoring/SKILL.md` |
| Tools, resources, schema reference | `.claude/skills/gitnexus-guide/SKILL.md` |
| Index, status, clean, wiki CLI commands | `.claude/skills/gitnexus-cli/SKILL.md` |

<!-- gitnexus:end -->
