#!/usr/bin/env python3
"""Convert every CamStudio project/database into one lossless COCO-compatible file."""

import argparse
import hashlib
import json
import os
import sqlite3
from concurrent.futures import ThreadPoolExecutor
from datetime import datetime, timezone
from pathlib import Path

DIRECTION_NAMES = {0: "in", 1: "out"}
DIRECTION_LABELS_HU = {0: "BEfelé", 1: "KIfelé"}


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def image_size(path: Path) -> tuple[int, int]:
    try:
        with path.open("rb") as stream:
            header = stream.read(24)
            if header.startswith(b"\x89PNG\r\n\x1a\n"):
                return int.from_bytes(header[16:20], "big"), int.from_bytes(header[20:24], "big")
            if not header.startswith(b"\xff\xd8"):
                return 0, 0
            stream.seek(2)
            scanned = 2
            while scanned < 1024 * 1024:
                marker_start = stream.read(1)
                scanned += 1
                if not marker_start:
                    break
                if marker_start != b"\xff":
                    continue
                marker = stream.read(1)
                scanned += 1
                while marker == b"\xff":
                    marker = stream.read(1)
                    scanned += 1
                if marker in (b"\xd8", b"\xd9"):
                    continue
                length_bytes = stream.read(2)
                scanned += 2
                if len(length_bytes) != 2:
                    break
                length = int.from_bytes(length_bytes, "big")
                if marker[0] in {0xC0, 0xC1, 0xC2, 0xC3, 0xC5, 0xC6, 0xC7, 0xC9, 0xCA, 0xCB, 0xCD, 0xCE, 0xCF}:
                    data = stream.read(5)
                    return int.from_bytes(data[3:5], "big"), int.from_bytes(data[1:3], "big")
                stream.seek(max(0, length - 2), 1)
                scanned += max(0, length - 2)
    except (FileNotFoundError, OSError, ValueError, IndexError):
        return 0, 0
    return 0, 0


def open_readonly(path: Path) -> sqlite3.Connection:
    return sqlite3.connect(f"{path.resolve().as_uri()}?mode=ro", uri=True)


def read_projects(root: Path) -> list[dict]:
    import xml.etree.ElementTree as et

    projects = []
    # The complete preflight scan found both CamStudio projects at the root.
    for path in sorted(root.glob("*.camu"), key=lambda p: str(p).lower()):
        project = et.parse(path).getroot()
        projects.append({
            "file": str(path),
            "project_name": project.get("projectname"),
            "version": project.get("version"),
            "title": project.findtext("title"),
            "database": project.findtext("database"),
            "data_path": project.findtext("datapath"),
        })
    return projects


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("root", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()
    root = args.root.resolve()
    databases = sorted(root.glob("*.db"), key=lambda p: p.name.lower())
    projects = read_projects(root)

    category_names = set()
    for database in databases:
        with open_readonly(database) as connection:
            category_names.update(row[0] for row in connection.execute(
                "SELECT DISTINCT Category FROM ENTITIES WHERE Category IS NOT NULL"))
    category_names = sorted(category_names)
    category_ids = {name: index + 1 for index, name in enumerate(category_names)}
    categories = [{"id": value, "name": name, "supercategory": "road_user"}
                  for name, value in category_ids.items()]

    images = []
    annotations = []
    category_nodes = []
    database_meta = []
    image_id = 0
    annotation_id = 0

    for database in databases:
        print(f"Reading {database.name}...", flush=True)
        connection = open_readonly(database)
        connection.row_factory = sqlite3.Row
        photo_map = {}
        photos = connection.execute("SELECT * FROM PHOTOS ORDER BY DateTime, Id").fetchall()
        absolute_paths = [root / Path(photo["RelativePath"] or "") / (photo["FileName"] or "")
                          for photo in photos]
        with ThreadPoolExecutor(max_workers=16) as executor:
            sizes = list(executor.map(image_size, absolute_paths, chunksize=64))
        for photo, absolute, (width, height) in zip(photos, absolute_paths, sizes):
            image_id += 1
            photo_map[photo["Id"]] = image_id
            relative = Path(photo["RelativePath"] or "") / (photo["FileName"] or "")
            images.append({
                "id": image_id,
                "file_name": relative.as_posix(),
                "width": width,
                "height": height,
                "date_captured": photo["DateTime"],
                "camstudio": {
                    "source_database": database.name,
                    "source_photo_id": photo["Id"],
                    "relative_path": photo["RelativePath"],
                    "original_file_name": photo["FileName"],
                    "is_empty": bool(photo["IsEmpty"]),
                    "is_duplicate": bool(photo["IsDuplum"]),
                    "is_not_readable": bool(photo["IsNotReadable"]),
                    "is_erasable": bool(photo["IsErasable"]),
                    "file_exists": absolute.is_file(),
                },
            })

        for entity in connection.execute("SELECT * FROM ENTITIES ORDER BY PhotoId, Number, Id"):
            annotation_id += 1
            direction = entity["Direction"]
            keywords = [value.strip() for value in (entity["Keywords"] or "").split(";") if value.strip()]
            annotations.append({
                "id": annotation_id,
                "image_id": photo_map[entity["PhotoId"]],
                "category_id": category_ids[entity["Category"]],
                "bbox": [entity["X"], entity["Y"], entity["Width"], entity["Height"]],
                "area": entity["Width"] * entity["Height"],
                "iscrowd": 0,
                "segmentation": [],
                "attributes": {
                    "direction": DIRECTION_NAMES.get(direction, "unknown"),
                    "direction_label_hu": DIRECTION_LABELS_HU.get(direction, "Ismeretlen"),
                    "number": entity["Number"],
                    "user_name": entity["UserName"],
                    "recognition": entity["Recognition"],
                    "keywords": keywords,
                    "timestamp": entity["TimeStamp"],
                },
                "camstudio": {
                    "source_database": database.name,
                    "source_entity_id": entity["Id"],
                    "source_photo_id": entity["PhotoId"],
                    "raw_direction": direction,
                    "raw_category": entity["Category"],
                    "raw_keywords": entity["Keywords"],
                },
            })

        for node in connection.execute("SELECT * FROM CATEGORIES ORDER BY Level, ParentId, Name, Id"):
            category_nodes.append({
                "source_database": database.name,
                "id": node["Id"],
                "parent_id": node["ParentId"],
                "name": node["Name"],
                "level": node["Level"],
            })
        database_meta.append({
            "file": database.name,
            "size": database.stat().st_size,
            "sha256": sha256(database),
            "photo_count": len(photo_map),
            "annotation_count": connection.execute("SELECT COUNT(*) FROM ENTITIES").fetchone()[0],
            "category_node_count": connection.execute("SELECT COUNT(*) FROM CATEGORIES").fetchone()[0],
        })
        connection.close()

    document = {
        "info": {
            "description": "Lossless normalized export of all CamStudio projects and SQLite databases",
            "version": "1.0",
            "date_created": datetime.now(timezone.utc).isoformat(),
            "format": "COCO 1.0 with namespaced CamStudio extensions",
        },
        "licenses": [],
        "images": images,
        "annotations": annotations,
        "categories": categories,
        "camstudio": {
            "schema_version": "1.0",
            "source_root": str(root),
            "projects": projects,
            "databases": database_meta,
            "category_nodes": category_nodes,
            "direction_mapping": {
                "0": {"name": "in", "label_hu": "BEfelé"},
                "1": {"name": "out", "label_hu": "KIfelé"},
            },
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    with args.output.open("w", encoding="utf-8") as stream:
        json.dump(document, stream, ensure_ascii=False, separators=(",", ":"))
    print(f"Wrote {args.output}: {len(images)} images, {len(annotations)} annotations", flush=True)


if __name__ == "__main__":
    main()
