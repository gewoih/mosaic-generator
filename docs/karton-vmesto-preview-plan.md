# Картон вместо превью (TODO п. 1, переформулированный)

**Статус: сделано 2026-09-04.** `dotnet test` 254/254; стенд 24 прогона (8 фото ×
15×15/A4/30×30, откус 6 мм) без падений; 30×30 выходит ~4114 px (≈348 dpi при печати 1:1).
Ниже — план как он был согласован.

## Почему

TODO п. 1 предлагал третий режим рендера «рядом с Preview и Scheme». В обсуждении
04.09 решили иначе: **реалистичное превью убрать совсем, вместо него — картон.**

Аргументы:

- **Preview в текущем виде вводит в заблуждение.** `ToneJitter = 4.0` L\* разводит два
  куска одного артикула до 8 L\*, при том что реальная минимальная разница между двумя
  смальтами в панно — 3–7 ΔE. Картинка показывает больше шума *внутри* зоны, чем
  настоящей разницы *между* зонами. Мастер-новичок, глядя на неё, решает «нормально,
  пёстро» — а на плоской распечатке те же зоны сольются, и это надо было увидеть до
  закупки плитки.

  Минимальная ΔE между двумя артикулами, попавшими в панно (откус 6 мм):

  | фото | 15×15 | A4 | 30×30 | худшая пара и доли панно |
  |---|---|---|---|---|
  | gull | **3,1** | **3,4** | 8,4 | DK13 3,4 % ↔ SX56 4,0 % |
  | gull-2 | **3,8** | **3,8** | **3,8** | FA05 10–15 % ↔ FB05 2,6–6,4 % |
  | gull-3 | 6,3 | **4,7** | 6,3 | SQ06 19,6 % ↔ TT06 7,6 % |
  | dolphin | **5,3** | **5,1** | **5,5** | SP15 15,9 % ↔ DK20 13,2 % |
  | dolphin-2 | 6,3 | **5,6** | 9,3 | TL01 5,2 % ↔ FB05 8,1 % |
  | human | 6,9 | **5,5** | 6,9 | FH01 11,0 % ↔ FI05 15,8 % |
  | landscape | 7,4 | 11,2 | 11,2 | — |
  | landscape-2 | 11,2 | 17,7 | 10,0 | — |

  В 17 раскладах из 24 есть пара ближе 8 ΔE, в девяти — ближе 5,5. Фальшивая вариация
  внутри артикула больше настоящей разницы между двумя смальтами.

  Разрешение: `PixelsPerStep = 24` у превью против 48 у схемы — на 30×30 при откусе 6 мм
  это 87 dpi, печать 1:1 мылит и лесенит край куска.
- **Плоский картон закрывает ту же работу, что и Preview** — композиция, распределение
  цвета, читаемость зон, — и делает это честно.
- **Фактурный рендер (глянец, поворот, рваный край) для инструмента-под-себя — это
  eye-candy.** По принципу «убрать параметр, а не сделать настраиваемым» и правилу
  «не вводить скрытый техдолг» его машинерия удаляется целиком, а не засыпает под
  нулевыми коэффициентами.
- **Набор — прямо поверх картона** (уточнено 04.09). Значит картон печатается 1:1,
  и разрешение 24 px/шаг (87 dpi на 30×30) больше не годится.

**Схема (`RenderScheme`, номер в каждом куске) остаётся** — она нужна для разбора
работы алгоритма (уточнено 03.09, см. [[karton-eto-prevyu-pryamoy-nabor]]). Не трогаем.

Границы зон и код артикула на картоне (TODO п. 3) — **следующим шагом**, не здесь.

## Что делаем

### Ядро

**`Core/Rendering/RenderOptions.cs`**
- Удалить поля `EdgeRoughness`, `RotationJitterDeg`, `SizeJitter`, `GlossJitter`,
  `ToneJitter`, `BrickBond` (последнее — мёртвое: staggering живёт в `Tessellation`,
  рендер его не читает).
- `Preview` → `Cartoon`, `PixelsPerStep = 96`. Комментарии переписать под плоский цвет.
- `Scheme` оставить, убрать из него зануления удалённых полей.

**`Core/Rendering/RenderGeometry.cs`**
- `Shape(...)` схлопывается до перевода номинального полигона в пиксели с клампом
  по полю. Убрать `DeterministicRandom`, `scale`/`rough`/`angle`.
- `FaceColours(...)` убрать; заливка = `plan.Palette.Colors[colorIndex].Lab.ToRgb()`.
- В `RenderedModule` больше не писать `GlossLow`/`GlossHigh`.
- `using MosaicGenerator.Core.Rendering` для `DeterministicRandom` — проверить, что
  файл `DeterministicRandom.cs` больше нигде не нужен; если нет — удалить.

**`Core/Rendering/RenderedModule.cs`** — убрать `GlossLow`, `GlossHigh`.

**`Core/Skia/SkiaMosaicRenderer.cs`**
- `RenderPreview` → `RenderCartoon`. Убрать ветку с `SKShader.CreateLinearGradient`
  (блик) — остаётся плоский `fill.Color`. `canvas.Clear(plan.JointColor)` + заливка
  полигонов оставляем: шов — это зазор между номинальными полигонами.

**`Core/Rendering/IMosaicRenderer.cs`** — `RenderPreview` → `RenderCartoon`.

**`Core/Pipeline/`**
- `MosaicGenerationOptions.cs`: `Preview` → `Cartoon`, дефолт `RenderOptions.Cartoon`.
- `MosaicGenerationService.cs`: `_options.Preview` → `_options.Cartoon`,
  `RenderPreview` → `RenderCartoon`.
- `MosaicResult.cs`: `PreviewPng` → `CartoonPng`, `RenderPlan Preview` → `Cartoon`.

### Веб

- `Options/MosaicOptions.cs`: `PreviewPixelsPerStep` (24) → `CartoonPixelsPerStep` (96).
- `appsettings.json`: тот же ключ.
- `Program.cs`: `Preview = RenderOptions.Preview with { ... }` → `Cartoon = RenderOptions.Cartoon with { ... }`.
- `Services/IResultStore.cs`: `enum ResultImage { Preview, Scheme }` → `{ Cartoon, Scheme }`.
- `Services/TempResultStore.cs`: `Save(... previewPng ...)`, `FileNameFor` →
  `cartoon.png` / `scheme.png`.
- `Services/StoredResult.cs`: `PreviewWidthPx/HeightPx/Dpi` → `Cartoon*`.
- `Controllers/MosaicController.cs`: `Preview(...)` экшен → `Cartoon(...)`, роут
  `result/{id}/cartoon.png`, скачивание `maket.png` → `karton.png`. `Describe(...)`
  и `result.Preview.*` → `result.Cartoon.*`.
- `Views/Mosaic/Result.cshtml`: первая `<figure>` — `@Url.Action("Cartoon", ...)`,
  подпись «Картон под набор», текст про «печать 100 %» оставить. Вторая `<figure>`
  (схема) — подпись «Схема (для разбора раскладки)».

### Стенд и тесты

- `tools/MosaicGenerator.Diag/Program.cs`: `result.PreviewPng` → `result.CartoonPng`,
  имя файла `{run.Name}-cartoon.png`. Комментарий шапки — «cartoon and scheme».
- `tests/.../Skia/SkiaMosaicRendererTests.cs`,
  `tests/.../Rendering/RenderGeometryTests.cs`,
  `tests/.../Rendering/ResolutionCapTests.cs`,
  `tests/.../Pipeline/MosaicGenerationServiceTests.cs`:
  - `RenderOptions.Preview` → `RenderOptions.Cartoon`, `RenderPreview` → `RenderCartoon`,
    `PreviewPng` → `CartoonPng`.
  - **Удалить** тесты, проверявшие именно jitter-геометрию: поворот/чип вершин
    (`RenderGeometryTests` ~44–101), разброс тона от `ToneJitter` (~152), градиент
    блика от `GlossJitter` (~173). Их предмет удалён.
  - Оставить и перенацелить на `Cartoon`: детерминизм рендера, запись физического
    масштаба в PNG, кламп разрешения (`ResolutionCapTests`), совпадение размеров
    картинки с `RenderPlan`.
  - Добавить тест: при `Cartoon` все куски одного `ColorIndex` залиты строго одним
    RGB (нулевая вариация внутри зоны).

## Разрешение

`PixelsPerStep = 96`. Проверка ёмкости:

| панно | откус | px/сторону (16 px/мм) | всего |
|---|---|---|---|
| 30×30 | 6 мм | 4800 × 4800 | 23 Мп |
| A4 | 6 мм | 3360 × 4752 | 16 Мп |
| 30×30 | 10 мм | 2880 × 2880 | 8 Мп |

Потолок `MaxTotalPixels = 30 000 000` и `MaxLongSidePx = 6000` не задеты; крупные
панно подстрахует `ResolveScale`. 60×60 при 6 мм упрётся в потолок и просядет по
dpi — приемлемо, это эталон, не рабочий размер.

## Как проверить

1. `dotnet test` — зелёный.
2. Стенд по восьми фото из `samples/`, откус 6 мм
   (`--sizes 15x15,21x29.7,30x30 --modules 6`):
   - в `*-cartoon.png` внутри одной цветовой зоны цвет строго один (пипеткой);
   - две любые соседние зоны различимы глазом на распечатке 1:1;
   - раскладка (геометрия кусков, шов) не поехала против прежнего прогона.
3. Веб: `result/{id}/cartoon.png` отдаётся, страница результата показывает картон
   первой картинкой и схему второй, «Скачать PNG» даёт `karton.png`.

## Хвост в TODO

После merge в п. 1 отметить «переформулирован и сделан», п. 3 переформулировать под
«границы зон + код артикула поверх картона», п. 17 — как есть (схема остаётся,
её читаемость — отдельно).
