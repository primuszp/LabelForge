#!/usr/bin/env python3
"""Create one source-agnostic, duplicate-free COCO dataset from CamStudio COCO."""

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path


def annotation_key(annotation: dict) -> tuple:
    bbox = tuple(round(float(value), 4) for value in annotation.get("bbox", []))
    segmentation = json.dumps(annotation.get("segmentation", []), sort_keys=True, separators=(",", ":"))
    return annotation["image_id"], annotation["category_id"], bbox, segmentation


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    with args.input.open(encoding="utf-8") as stream:
        source = json.load(stream)

    old_to_new_image_id = {}
    images = []
    for new_id, image in enumerate(source["images"], 1):
        old_to_new_image_id[image["id"]] = new_id
        camstudio = image.get("camstudio", {})
        clean = {
            "id": new_id,
            "file_name": image["file_name"],
            "width": image["width"],
            "height": image["height"],
        }
        if image.get("date_captured"):
            clean["date_captured"] = image["date_captured"]
        clean["attributes"] = {
            "is_empty": bool(camstudio.get("is_empty", False)),
            "is_not_readable": bool(camstudio.get("is_not_readable", False)),
            "is_erasable": bool(camstudio.get("is_erasable", False)),
        }
        images.append(clean)

    seen = set()
    annotations = []
    removed_annotations = []
    for annotation in source["annotations"]:
        key = annotation_key(annotation)
        if key in seen:
            removed_annotations.append(annotation)
            continue
        seen.add(key)
        annotations.append({
            "id": len(annotations) + 1,
            "image_id": old_to_new_image_id[annotation["image_id"]],
            "category_id": annotation["category_id"],
            "bbox": annotation.get("bbox", []),
            "area": annotation.get("area", 0),
            "iscrowd": annotation.get("iscrowd", 0),
            "segmentation": annotation.get("segmentation", []),
            "attributes": annotation.get("attributes", {}),
        })

    clean_document = {
        "info": {
            "description": "Unified duplicate-free CamStudio training dataset",
            "version": "1.0",
            "date_created": datetime.now(timezone.utc).isoformat(),
            "source_format": "CamStudio normalized COCO",
            "image_count": len(images),
            "annotation_count": len(annotations),
        },
        "licenses": source.get("licenses", []),
        "images": images,
        "annotations": annotations,
        "categories": source["categories"],
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix(args.output.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8") as stream:
        json.dump(clean_document, stream, ensure_ascii=False, separators=(",", ":"))
    temporary.replace(args.output)

    audit_path = args.output.with_name(args.output.stem + ".annotation-duplicates-audit.json")
    with audit_path.open("w", encoding="utf-8") as stream:
        json.dump({"removed_annotations": removed_annotations}, stream, ensure_ascii=False, separators=(",", ":"))
    print(f"Wrote {args.output}: {len(images)} images, {len(annotations)} annotations, "
          f"{len(removed_annotations)} duplicate annotations removed")


if __name__ == "__main__":
    main()
