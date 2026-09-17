#!/usr/bin/env python3
"""Собирает полный набор образов релиза и закрепляет каждый по digest.

Зачем это нужно. Требование «не должно быть api нового и client старого» нельзя выполнить дисциплиной:
джоба сборки модуля не запускается, если его пути не менялись, и в наивной схеме релиз собирался бы из
того, что случайно пересобралось. Здесь комплект всегда полный: свежий digest берётся из сборки этого
прогона, а для непересобранного модуля — тот, на который сейчас смотрит тег main. Если ни того, ни
другого нет (самый первый запуск), скрипт честно падает, а не выкатывает половину.

Чужие образы (postgres, MinIO, mc, llama.cpp, vLLM) закрепляются по digest по той же причине, по какой
мы не пишем :latest: тег в чужом реестре изменяем, и апстрим может подменить базовый образ БД между
двумя выкатками, не тронув ни строчки в репозитории.

Список образов не зашит, а берётся из отрендеренных манифестов: добавили сервис и забыли про CI —
это не пройдёт незамеченным.

ВАЖНО про ключи. Раньше digest складывался по имени репозитория без тега, и это было ошибкой: все
варианты llama.cpp живут в ОДНОМ репозитории ghcr.io/ggml-org/llama.cpp и различаются только тегом
(server, server-cuda). Два разных образа писали в один ключ, побеждал последний по алфавиту, и профиль
на процессоре молча получал digest CUDA-образа. Поэтому ключ здесь — полная ссылка с тегом, а подстановка
в kustomize идёт ПО ОДНОМУ МОДУЛЮ: трансформер images сопоставляет образы по имени без тега, так что
два варианта одного репозитория обязаны жить в разных оверлеях и получать digest'ы раздельно.

    python3 scripts/resolve-images.py rendered/ --registry docker.io --prefix not2ilya2work \\
        --digest flow-api=sha256:... --sha <commit> --out release.json

Коды возврата: 0 — комплект собран, 1 — что-то не разрешилось, 2 — ошибка самого скрипта.
"""

import argparse
import json
import os
import re
import subprocess
import sys
from pathlib import Path

IMAGE_LINE = re.compile(r"^\s*image:\s*[\"']?(?P<image>\S+?)[\"']?\s*$")


def images_by_module(directory: Path) -> dict[str, list[str]]:
    """Имя файла — имя модуля: рендер кладёт каждый оверлей в <модуль>.yaml."""
    found: dict[str, list[str]] = {}
    for file in sorted(directory.rglob("*.yaml")):
        module = file.stem
        seen: set[str] = set()
        for line in file.read_text(encoding="utf-8").splitlines():
            match = IMAGE_LINE.match(line)
            if match:
                seen.add(match.group("image"))
        if seen:
            found[module] = sorted(seen)
    return found


def repository_of(image: str) -> str:
    """Имя без тега и без digest. Порт реестра (host:5000/repo) не должен сойти за тег."""
    without_digest = image.split("@", 1)[0]
    head, _, tail = without_digest.rpartition("/")
    name = tail.split(":", 1)[0]
    return f"{head}/{name}" if head else name


DIGEST_LINE = re.compile(r"^Digest:\s+(?P<digest>sha256:[0-9a-f]{64})\s*$", re.MULTILINE)


def digest_of(reference: str) -> str | None:
    """Спрашиваем реестр, а не докер-демон: образа локально может не быть вовсе.

    Две попытки, и это не перестраховка. Шаблон --format читается короче и не зависит от того, как
    выглядит человекочитаемый вывод, но существуют сборки buildx (в частности та, что едет с Docker
    Desktop 28), где этот флаг молча игнорируется и печатается обычная таблица. Без второй попытки
    скрипт на такой машине объявлял бы недоступным каждый чужой образ подряд — диагноз, который
    отправляет искать проблему в реестре и в сети, а она в клиенте.
    """
    formatted = subprocess.run(
        ["docker", "buildx", "imagetools", "inspect", "--format", "{{.Manifest.Digest}}", reference],
        capture_output=True, text=True,
    )
    if formatted.returncode != 0:
        return None
    digest = formatted.stdout.strip()
    if digest.startswith("sha256:"):
        return digest

    match = DIGEST_LINE.search(formatted.stdout)
    return match.group("digest") if match else None


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("rendered", type=Path, help="каталог с отрендеренными манифестами")
    parser.add_argument("--registry", required=True)
    parser.add_argument("--prefix", required=True)
    parser.add_argument("--sha", default=os.environ.get("GITHUB_SHA", ""))
    parser.add_argument("--inherit-tag", default="main",
                        help="тег, с которого наследуется digest непересобранного модуля")
    parser.add_argument("--digest", action="append", default=[], metavar="МОДУЛЬ=DIGEST",
                        help="digest из сборки этого прогона; пустое значение = модуль не пересобирался")
    parser.add_argument("--out", type=Path, default=Path("release.json"))
    args = parser.parse_args()

    if not args.rendered.is_dir():
        print(f"нет каталога с манифестами: {args.rendered}", file=sys.stderr)
        return 2

    # Ключи --digest задают заодно и список СВОИХ образов. Это важнее, чем кажется: в манифестах свои
    # образы названы коротко (flow-api), а не полным путём в реестре — полное имя приписывает уже
    # kustomize вместе с digest'ом. Узнавать своё по префиксу реестра здесь нельзя: в отрендеренных
    # манифестах этого префикса ещё нет, и все три наших образа уехали бы в ветку «чужой образ»,
    # где скрипт честно упал бы на попытке спросить у Docker Hub про образ flow-api:local.
    fresh: dict[str, str] = {}
    own_modules: set[str] = set()
    for pair in args.digest:
        module, _, value = pair.partition("=")
        module = module.strip()
        if not module:
            continue
        own_modules.add(module)
        if value.strip():
            fresh[module] = value.strip()

    per_module = images_by_module(args.rendered)
    if not per_module:
        print("в манифестах не нашлось ни одного образа — проверять нечего", file=sys.stderr)
        return 2

    # image -> (имя, по которому kustomize сопоставляет образ; полный путь в реестре; digest)
    resolved: dict[str, tuple[str, str, str]] = {}
    rows: list[tuple[str, str, str, str]] = []
    failed = False

    for module, images in per_module.items():
        for image in images:
            # Один и тот же образ встречается в нескольких модулях (например alpine у джоб) — спрашиваем
            # реестр один раз, иначе на каждую выкатку получаем лишние обращения и лишние минуты.
            if image in resolved:
                continue

            match_name = repository_of(image)
            short_name = match_name.rsplit("/", 1)[-1]

            if short_name in own_modules:
                # Полное имя появляется только здесь: в манифесте лежит короткое, и именно на него
                # kustomize будет искать совпадение, подставляя вместо него реестр, префикс и digest.
                target = f"{args.registry}/{args.prefix}/{short_name}"
                digest = fresh.get(short_name)
                source = "собран в этом прогоне"
                if not digest:
                    digest = digest_of(f"{target}:{args.inherit_tag}")
                    source = f"унаследован с тега {args.inherit_tag}"
                if not digest:
                    print(f"::error title=resolve-images::для {target} нет ни свежего digest, ни тега "
                          f"{args.inherit_tag} — собери модуль хотя бы раз на main")
                    failed = True
                    continue
            else:
                target = match_name
                digest = digest_of(image)
                source = "чужой образ, закреплён по digest"
                if not digest:
                    print(f"::error title=resolve-images::образ {image} недоступен в реестре — "
                          f"проверь тег и доступ к реестру")
                    failed = True
                    continue

            resolved[image] = (match_name, target, digest)
            rows.append((module, image, digest, source))

    # Для каждого модуля — готовые аргументы `kustomize edit set image`. Джобе рендера остаётся
    # скормить их своему оверлею и ничего не вычислять: вычисления на стороне шелла — это и был
    # тот самый путь, которым CPU-профиль однажды получил digest CUDA-образа.
    modules = {
        module: [f"{resolved[image][0]}={resolved[image][1]}@{resolved[image][2]}"
                 for image in images if image in resolved]
        for module, images in per_module.items()
    }

    args.out.write_text(
        json.dumps({"sha": args.sha,
                    "images": {image: digest for image, (_, _, digest) in resolved.items()},
                    "modules": modules},
                   indent=2, ensure_ascii=False) + "\n",
        encoding="utf-8",
    )

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    report = ["### Набор образов релиза", "",
              "| Модуль | Образ | Digest | Откуда |", "|--------|-------|--------|--------|"]
    report += [f"| {module} | `{image}` | `{digest[:19]}…` | {source} |"
               for module, image, digest, source in rows]
    text = "\n".join(report) + "\n"
    if summary:
        with open(summary, "a", encoding="utf-8") as handle:
            handle.write(text)
    print(text)

    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
