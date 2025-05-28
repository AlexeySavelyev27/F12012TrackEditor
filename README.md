# F12012TrackEditor
A tool for editing existing and creating new tracks for F1 2012
See [docs/file-format-map.md](docs/file-format-map.md) for a description of the
included Melbourne track files.

## Utilities

`bxml2xml.py` converts Codemasters BXML files to regular XML.  It can also
detect and pass through normal XML files. Usage:

```bash
python3 bxml2xml.py <input.bxml> [output.xml]
```

The tool handles the simple BXML variant starting with `00 42 58 4D` and also
fully supports RBXML files with the header `1A 22 52 72`.  RBXML files are
parsed using their string, node and attribute tables to reconstruct the complete
XML tree.  If the input already contains plain XML (with or without a UTF-8
BOM) it is returned unchanged.

`pssg_info.py` reads the header of a PSSG archive and shows basic
information about the file:

```bash
python3 pssg_info.py melbourne/objects.pssg
```

## PSSG Format

- Файлы PSSG используются движком EGO как контейнеры для моделей, текстур и анимаций.
- Заголовок начинается с сигнатуры `PSSG` и трех 32‑битных чисел (размер файла,
  смещение таблицы строк и смещение корневого узла) в big-endian.
- После них обычно следуют значения `1` и `7`, одинаковые у всех исследованных архивов.
- Таблица строк расположена по смещению `0x239`, а корневой узел — по `0x12b`
  (эти значения совпадали у всех файлов `melbourne/`).
- Узлы записываются иерархически: длина имени (32 бита), имя в ASCII, затем два
  32‑битных поля (первое предположительно флаги, второе — число дочерних элементов
  или атрибутов).
- Атрибуты ссылаются на строки из таблицы по их индексам. Многие строки содержат
  названия текстур и мешей вроде `objects.pssg#concrete_green_a_01_d.tga`.
- Внешнее сжатие отсутствует, все данные хранятся напрямую внутри архива.

Пример запуска утилиты для получения заголовка:
```bash
python3 pssg_info.py melbourne/objects.pssg
python3 pssg_info.py melbourne/route_0/objects.ens
```

## Track Data Overview

The `melbourne/` directory contains all resources for the Melbourne circuit and
illustrates the variety of formats used by the EGO engine:

- Plain XML like `ParcFerme.xml` describe simple object placement.
- `RacingLine.xml` is a much larger file with settings, corner ranges,
  suggested gears, apex points and detailed pit-lane nodes.
- Many `*.xml` files are actually binary BXML variants (e.g. `basewind2.xml`,
  `weather_fx.xml`) which can be converted with `bxml2xml.py`.
- Container archives (`*.pssg`, `*.ens`, `*.jpk`) store models, textures and
  other assets.
- Additional binary tables include collision meshes (`*.clm`), boundary and
  reset lines (`*.cqtc`), visibility data (`*.vis`), lookup tables (`*.lut`)
  and others.

В каталоге находится 61 файл, из них 29 — XML (часть в бинарном формате), а
остальные представлены PSSG/ENS и несколькими типами BIN/CLM/CQTC.  Полный
список заголовков приведен в
[docs/file-format-map.md](docs/file-format-map.md).
