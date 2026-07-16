#!/usr/bin/env python3
"""Remove CamStudio-marked duplicate photos from the active COCO dataset without data loss."""

import argparse
import json
from datetime import datetime, timezone
from pathlib import Path


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("input", type=Path)
    parser.add_argument("output", type=Path)
    args = parser.parse_args()

    with args.input.open(encoding="utf-8") as stream:
        document = json.load(stream)

    duplicate_images = [
        image for image in document["images"]
        if image.get("camstudio", {}).get("is_duplicate") is True
    ]
    duplicate_ids = {image["id"] for image in duplicate_images}
    duplicate_annotations = [
        annotation for annotation in document["annotations"]
        if annotation["image_id"] in duplicate_ids
    ]

    document["images"] = [image for image in document["images"] if image["id"] not in duplicate_ids]
    document["annotations"] = [
        annotation for annotation in document["annotations"]
        if annotation["image_id"] not in duplicate_ids
    ]
    camstudio = document.setdefault("camstudio", {})
    camstudio["deduplication"] = {
        "method": "CamStudio PHOTOS.IsDuplum flag",
        "date_created": datetime.now(timezone.utc).isoformat(),
        "active_image_count": len(document["images"]),
        "active_annotation_count": len(document["annotations"]),
        "excluded_image_count": len(duplicate_images),
        "excluded_annotation_count": len(duplicate_annotations),
        "audit_file": args.output.stem + ".duplicates-audit.json",
        "note": "Excluded records are stored in the audit file; no geometry was transferred between non-identical images."
    }
    audit = {
        "info": {
            "description": "CamStudio duplicate records excluded from the active COCO dataset",
            "source_dataset": str(args.input),
            "active_dataset": str(args.output),
        },
        "images": duplicate_images,
        "annotations": duplicate_annotations,
    }

    args.output.parent.mkdir(parents=True, exist_ok=True)
    temporary = args.output.with_suffix(args.output.suffix + ".tmp")
    with temporary.open("w", encoding="utf-8") as stream:
        json.dump(document, stream, ensure_ascii=False, separators=(",", ":"))
    temporary.replace(args.output)
    audit_path = args.output.with_name(args.output.stem + ".duplicates-audit.json")
    audit_temporary = audit_path.with_suffix(audit_path.suffix + ".tmp")
    with audit_temporary.open("w", encoding="utf-8") as stream:
        json.dump(audit, stream, ensure_ascii=False, separators=(",", ":"))
    audit_temporary.replace(audit_path)
    print(
        f"Wrote {args.output}: {len(document['images'])} active images, "
        f"{len(document['annotations'])} active annotations, "
        f"{len(duplicate_images)} audited duplicates"
    )


if __name__ == "__main__":
    main()
