#!/usr/bin/env python3
"""Сверяет пресет моделей llama-server с остальным репозиторием.

Зачем. Пресет docker/ai/models.ini — единственное место, где записано, какие модели поднимает
router-режим и в каком режиме работает каждая. Он связан с тремя другими местами, и все три связи
рвутся молча:

  1. Имя секции = значение EMBEDDINGS_MODEL / RERANKER_MODEL / VISION_MODEL. Именно его .NET кладёт в "model"
     каждого запроса, и по нему роутер выбирает процесс. Разъехались — поиск отвечает ошибкой
     про неизвестную модель, а выглядит это как «сломался эмбеддер».
  2. Путь в ключе model = /models/<файл> = EMBEDDINGS_MODEL_FILE / RERANKER_MODEL_FILE /
     VISION_MODEL_FILE. По этим переменным качают веса pull-models.sh и Job закачки; сам сервер их
     больше не видит. Разъехались — сервер ищет файл, которого никто не скачал, и падает на старте.
     У визуальной модели имя файла вдобавок входит в ModelVersion, то есть управляет переиндексацией.
  2a. У визуальной модели обязателен ключ mmproj = /models/<VISION_MMPROJ_FILE>. Без проектора
     сервер поднимет только текстовую половину модели: запросы с картинкой будут падать с «Failed to
     tokenize prompt», а в /props не окажется ни media_marker, ни modalities.vision.
  3. Режимы (embedding, reranking, pooling) обязаны быть ПЕР-МОДЕЛЬНЫМИ. Глобальная секция [*]
     с любым из них форсит pooling_type на все модели сразу: эмбеддер начинает отдавать нули, ошибок
     в логе нет, поиск тихо выдаёт мусор (llama.cpp#21256, закрыт как not planned).
  4. Размеров буферов (ctx-size, batch-size, ubatch-size) в пресете быть не должно вовсе: они зависят
     от машины, а файл общий у кластера и локального стенда, и задаёт их профиль железа аргументами
     роутера. Аргумент командной строки перебивает ключ пресета молча — то есть значение в файле
     не сработало бы, но выглядело бы как настройка. Именно на этом модуль и встал при первой выкатке
     на карту: ubatch-size 8192 из процессорного пресета просит 4932 МиБ видеопамяти.

Поэтому проверка тут, а не в голове у того, кто правит .env.example.

    python3 scripts/check-ai-preset.py

Коды возврата: 0 — сошлось, 1 — расхождение, 2 — ошибка самого скрипта.
"""

import re
import sys
from pathlib import Path

PRESET = Path("docker/ai/models.ini")
ENV_EXAMPLE = Path(".env.example")

# Ключи режимов: в глобальной секции их быть не должно ни под каким видом.
MODE_KEYS = {"embedding", "embeddings", "reranking", "rerank", "pooling"}

# Ключи размеров буферов: их не должно быть НИ В ОДНОЙ секции — ни в глобальной, ни в модельной.
# Место этих значений — профиль железа (k8s/base/ai/ai.yaml, k8s/components/gpu-nvidia, .env).
SIZE_KEYS = {"ctx-size", "batch-size", "ubatch-size"}

# Пары «переменная имени модели, переменная имени файла».
PAIRS = [
    ("EMBEDDINGS_MODEL", "EMBEDDINGS_MODEL_FILE"),
    ("RERANKER_MODEL", "RERANKER_MODEL_FILE"),
    ("VISION_MODEL", "VISION_MODEL_FILE"),
]

# Модель с проектором: имя переменной с весами проектора и ключ пресета, в котором они обязаны стоять.
VISION_MODEL_VAR = "VISION_MODEL"
VISION_MMPROJ_VAR = "VISION_MMPROJ_FILE"

SECTION = re.compile(r"^\s*\[(?P<name>[^\]]+)\]\s*$")
ENTRY = re.compile(r"^\s*(?P<key>[A-Za-z_][A-Za-z0-9_.-]*)\s*=\s*(?P<value>.*?)\s*$")


def parse_ini(path: Path) -> dict[str, dict[str, str]]:
    """Свой разборщик, а не configparser: нам нужны только секции и ключи, а строки с ';' в значении
    и дубли ключей configparser трактует по-своему. Лишняя зависимость ради этого не нужна."""
    sections: dict[str, dict[str, str]] = {}
    current = "__root__"
    sections[current] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith(";") or stripped.startswith("#"):
            continue
        match = SECTION.match(line)
        if match:
            current = match.group("name")
            sections.setdefault(current, {})
            continue
        entry = ENTRY.match(line)
        if entry:
            sections[current][entry.group("key").lower()] = entry.group("value")
    return sections


def parse_env(path: Path) -> dict[str, str]:
    values: dict[str, str] = {}
    for line in path.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped or stripped.startswith("#") or "=" not in stripped:
            continue
        name, _, value = stripped.partition("=")
        values[name.strip()] = value.strip()
    return values


def problem(text: str, file: str) -> None:
    print(f"  {text}")
    print(f"::error file={file},title=пресет моделей::{text}")


def main() -> int:
    for path in (PRESET, ENV_EXAMPLE):
        if not path.is_file():
            print(f"не найден файл: {path}", file=sys.stderr)
            return 2

    sections = parse_ini(PRESET)
    env = parse_env(ENV_EXAMPLE)
    failed = False

    # 1. Режимы не должны жить в глобальной секции.
    for name in ("*", "__root__"):
        leaked = sorted(MODE_KEYS & set(sections.get(name, {})))
        if leaked:
            where = "секции [*]" if name == "*" else "шапке файла до первой секции"
            problem(f"в {where} заданы режимы {', '.join(leaked)} — они достанутся всем моделям "
                    f"сразу, и эмбеддер начнёт молча отдавать нули (llama.cpp#21256)", str(PRESET))
            failed = True

    # 1a. Размеры буферов не должны жить в пресете вовсе — ни глобально, ни в модельной секции.
    for name, keys in sections.items():
        leaked = sorted(SIZE_KEYS & set(keys))
        if leaked:
            where = "шапке файла" if name == "__root__" else f"секции [{name}]"
            problem(f"в {where} заданы размеры буферов {', '.join(leaked)} — они зависят от машины "
                    f"и приезжают аргументами роутера из профиля железа, а аргумент перебивает ключ "
                    f"пресета молча. Значение отсюда не сработает, но будет выглядеть настройкой: "
                    f"уберите его и правьте k8s/components/gpu-nvidia (карта), k8s/base/ai/ai.yaml "
                    f"и .env (процессор)", str(PRESET))
            failed = True

    # 2. Каждой паре переменных — своя секция с совпадающим именем и файлом.
    model_sections = {n: keys for n, keys in sections.items() if n not in ("*", "__root__")}
    used: set[str] = set()

    for model_var, file_var in PAIRS:
        model = env.get(model_var, "")
        file_name = env.get(file_var, "")
        if not model or not file_name:
            problem(f"в .env.example нет {model_var} или {file_var} — сверять пресет не с чем",
                    str(ENV_EXAMPLE))
            failed = True
            continue

        if model not in model_sections:
            problem(f"{model_var}={model}, но секции [{model}] в пресете нет. Роутер выбирает модель "
                    f"по этому имени, значит запросы уйдут в никуда. Есть секции: "
                    f"{', '.join(sorted(model_sections)) or '— ни одной'}", str(PRESET))
            failed = True
            continue

        used.add(model)
        expected = f"/models/{file_name}"
        actual = model_sections[model].get("model", "")
        if actual != expected:
            problem(f"в секции [{model}] путь к весам «{actual}», а {file_var}={file_name} даёт "
                    f"«{expected}». Веса качают по переменной, а грузит сервер по пресету — "
                    f"разойдутся, и сервер не найдёт файл", str(PRESET))
            failed = True

    # 2a. Визуальной модели нужен проектор: без mmproj картинку принять нечем.
    vision = env.get(VISION_MODEL_VAR, "")
    mmproj_file = env.get(VISION_MMPROJ_VAR, "")
    if vision and vision in model_sections:
        expected = f"/models/{mmproj_file}" if mmproj_file else ""
        actual = model_sections[vision].get("mmproj", "")
        if not mmproj_file:
            problem(f"в .env.example нет {VISION_MMPROJ_VAR} — веса проектора никто не скачает, "
                    f"а без них визуальная модель принимает только текст", str(ENV_EXAMPLE))
            failed = True
        elif actual != expected:
            problem(f"в секции [{vision}] ключ mmproj — «{actual or '— его нет вовсе'}», а "
                    f"{VISION_MMPROJ_VAR}={mmproj_file} даёт «{expected}». Без совпадения сервер "
                    f"поднимет только текстовую половину модели: запросы с картинкой будут падать "
                    f"с «Failed to tokenize prompt»", str(PRESET))
            failed = True

    # 3. Лишняя секция — это модель, которую никто не позовёт: лимит --models-max и память она займёт,
    #    а запросов к ней не будет, потому что имя не совпадает ни с одной настройкой приложения.
    for extra in sorted(set(model_sections) - used):
        problem(f"секция [{extra}] не соответствует ни одной переменной из "
                f"{', '.join(v for v, _ in PAIRS)} — приложение эту модель никогда не запросит",
                str(PRESET))
        failed = True

    if failed:
        return 1

    print(f"пресет сошёлся: {len(used)} модели, режимы пер-модельные, пути к весам и проектору "
          f"совпадают с переменными закачки")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
