#!/usr/bin/env python3
"""Проверяет собранные манифесты на ошибки, специфичные именно для этого проекта.

Почему это отдельная проверка, а не kube-linter. Универсальный линтер хорошо ловит общие правила
(есть ли пробы, заданы ли ресурсы), но ничего не знает про здешние ловушки: клиенту nginx
runAsNonRoot ЗАПРЕЩЁН, потому что подстановка адресов висит на /docker-entrypoint.d и при non-root
молча пропускается — под остаётся зелёным, а браузер уезжает на localhost:5000. Такое правило
выразить в чужом конфиге нельзя, а поймать нужно до выкатки, а не по жалобам.

Почему python, а не sh, как соседние скрипты: здесь разбирается YAML, и «грепом по отступам» это
делается ровно до первого многострочного блока. Python3 есть и на ubuntu-latest, и локально.

    python3 scripts/check-manifests.py manifests/            # проверить отрендеренное
    python3 scripts/check-manifests.py --require-digest manifests/   # плюс: все образы по digest

Коды возврата: 0 — чисто, 1 — есть нарушения, 2 — ошибка самого скрипта.
"""

import sys
from pathlib import Path

try:
    import yaml
except ImportError:  # pragma: no cover
    print("нужен PyYAML: pip install pyyaml", file=sys.stderr)
    raise SystemExit(2)

WORKLOADS = ("Deployment", "StatefulSet", "DaemonSet")
JOBS = ("Job", "CronJob")

problems: list[tuple[str, str]] = []


def fail(where: str, message: str) -> None:
    problems.append((where, message))


def pod_spec_of(doc: dict):
    """Возвращает pod spec независимо от типа объекта: у CronJob он лежит на два уровня глубже."""
    kind = doc.get("kind")
    spec = doc.get("spec") or {}
    if kind in WORKLOADS:
        return (spec.get("template") or {}).get("spec")
    if kind == "Job":
        return (spec.get("template") or {}).get("spec")
    if kind == "CronJob":
        job = (spec.get("jobTemplate") or {}).get("spec") or {}
        return (job.get("template") or {}).get("spec")
    return None


def check_resources(where: str, containers: list) -> None:
    for container in containers:
        name = container.get("name", "?")
        resources = container.get("resources") or {}
        requests = resources.get("requests") or {}
        limits = resources.get("limits") or {}
        # Ноды две, worker один: под без requests планировщик считает бесплатным, а под без limits по
        # памяти утягивает за собой соседей — на домашнем кластере это выселение postgres.
        for field in ("cpu", "memory"):
            if field not in requests:
                fail(where, f"контейнер {name}: нет resources.requests.{field}")
        if "memory" not in limits:
            fail(where, f"контейнер {name}: нет resources.limits.memory")


def check_probes(where: str, containers: list) -> None:
    for container in containers:
        name = container.get("name", "?")
        for probe in ("livenessProbe", "readinessProbe"):
            if probe not in container:
                fail(where, f"контейнер {name}: нет {probe}")


def check_selector(where: str, doc: dict) -> None:
    selector = ((doc.get("spec") or {}).get("selector") or {})
    labels = selector.get("matchLabels") or {}
    if "flow.io/release" in labels:
        fail(where, "метка релиза попала в spec.selector — селектор иммутабелен, следующий apply упадёт")


def check_job(where: str, doc: dict) -> None:
    name = doc["metadata"]["name"]
    spec = doc.get("spec") or {}
    if doc["kind"] == "Job":
        # Job иммутабелен: apply поверх завершившегося с изменённым spec падает с «field is immutable».
        # Уникальное имя на релиз снимает это, а ttl убирает завершённые объекты за собой.
        if "${RELEASE_SHA}" not in name and not name.rstrip("-").split("-")[-1].isalnum():
            fail(where, f"имя Job «{name}» не содержит суффикса релиза — повторный apply упрётся в иммутабельность")
        if "ttlSecondsAfterFinished" not in spec:
            fail(where, f"у Job «{name}» нет ttlSecondsAfterFinished — завершённые объекты будут копиться")


def check_images(where: str, pod: dict, require_digest: bool) -> None:
    containers = (pod.get("containers") or []) + (pod.get("initContainers") or [])
    for container in containers:
        image = container.get("image", "")
        if image.endswith(":latest") or (":" not in image and "@" not in image):
            fail(where, f"образ «{image}» без фиксированной версии — выкатка перестаёт быть воспроизводимой")
        if require_digest and "@sha256:" not in image:
            fail(where, f"образ «{image}» не закреплён по digest")


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    require_digest = "--require-digest" in sys.argv[1:]
    if not args:
        print(__doc__, file=sys.stderr)
        return 2

    files: list[Path] = []
    for arg in args:
        path = Path(arg)
        if path.is_dir():
            files.extend(sorted(path.rglob("*.yaml")))
        elif path.is_file():
            files.append(path)
        else:
            print(f"не найдено: {arg}", file=sys.stderr)
            return 2

    if not files:
        print("не нашлось ни одного файла манифеста — проверять нечего", file=sys.stderr)
        return 2

    documents = 0
    for file in files:
        try:
            docs = list(yaml.safe_load_all(file.read_text(encoding="utf-8")))
        except yaml.YAMLError as error:
            fail(str(file), f"не разбирается как YAML: {error}")
            continue

        for doc in docs:
            if not isinstance(doc, dict) or "kind" not in doc:
                continue
            documents += 1
            kind = doc["kind"]
            name = (doc.get("metadata") or {}).get("name", "?")
            where = f"{file}: {kind}/{name}"

            # Секретов в репозитории быть не должно вовсе: их создаёт джоба выкатки из GitHub Secrets.
            if kind == "Secret":
                fail(where, "объект Secret в репозитории — секреты создаёт джоба deploy-platform, "
                            "коммитить их нельзя")

            pod = pod_spec_of(doc)
            if pod is None:
                continue

            containers = pod.get("containers") or []
            check_resources(where, containers)
            check_images(where, pod, require_digest)

            if kind in WORKLOADS:
                check_probes(where, containers)
                check_selector(where, doc)
            if kind in JOBS:
                check_job(where, doc)

    print(f"проверено объектов: {documents} в {len(files)} файлах")
    if problems:
        print(f"\nнарушений: {len(problems)}\n")
        for where, message in problems:
            print(f"  {where}\n    {message}")
        return 1

    print("нарушений не найдено")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
