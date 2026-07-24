#!/usr/bin/env python3
"""Extract stable pagination/wrapping evidence from generated report PDFs.

This is a CI/test helper only. It does not become a runtime dependency of the
desktop or browser-server products.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import logging
import platform
import re
import sys
import unicodedata
import warnings
from pathlib import Path
from typing import Any

import pdfplumber


# Chrome Type3 glyph tops vary by up to 2.58pt across OSes; 3pt merges distinct invoice columns.
LINE_TOP_TOLERANCE_POINTS = 2.75
OVERLAP_TOLERANCE_POINTS = 1.0


def parse_arguments() -> argparse.Namespace:
    parser = argparse.ArgumentParser(
        description="Extract line-wrap signatures and text-overlap evidence from report PDFs."
    )
    parser.add_argument("root", type=Path, help="PDF file or directory to scan recursively.")
    return parser.parse_args()


def normalize_text(value: str) -> str:
    normalized = unicodedata.normalize("NFKC", value or "")
    return re.sub(r"\s+", " ", normalized).strip()


def sha256_text(value: str) -> str:
    return hashlib.sha256(value.encode("utf-8")).hexdigest().upper()


def wrapping_shape(value: str) -> str:
    """Describe line composition without depending on PDF Unicode mappings."""
    shape: list[str] = []
    for character in unicodedata.normalize("NFKD", normalize_text(value)):
        category = unicodedata.category(character)
        if category.startswith("M"):
            continue
        shape.append(" " if character.isspace() else category[0])
    return re.sub(r"\s+", " ", "".join(shape)).strip()


def group_words_into_lines(words: list[dict[str, Any]]) -> list[dict[str, Any]]:
    lines: list[dict[str, Any]] = []
    for word in sorted(words, key=lambda item: (float(item["top"]), float(item["x0"]))):
        top = float(word["top"])
        if not lines or abs(top - float(lines[-1]["anchorTop"])) > LINE_TOP_TOLERANCE_POINTS:
            lines.append({"anchorTop": top, "words": [word]})
            continue

        lines[-1]["words"].append(word)
        line_words = lines[-1]["words"]
        lines[-1]["anchorTop"] = sum(float(item["top"]) for item in line_words) / len(line_words)

    result: list[dict[str, Any]] = []
    for line in lines:
        line_words = sorted(line["words"], key=lambda item: float(item["x0"]))
        text = normalize_text(" ".join(str(item.get("text") or "") for item in line_words))
        if not text:
            continue
        result.append(
            {
                "top": round(float(line["anchorTop"]), 2),
                "left": round(min(float(item["x0"]) for item in line_words), 2),
                "right": round(max(float(item["x1"]) for item in line_words), 2),
                "textHash": sha256_text(text),
                "wrapHash": sha256_text(wrapping_shape(text)),
                "textLength": len(text),
            }
        )
    return result


def find_text_overlaps(words: list[dict[str, Any]], page_number: int) -> list[dict[str, Any]]:
    overlaps: list[dict[str, Any]] = []
    for index, left in enumerate(words):
        for right in words[index + 1 :]:
            vertical_overlap = min(float(left["bottom"]), float(right["bottom"])) - max(
                float(left["top"]), float(right["top"])
            )
            if vertical_overlap <= OVERLAP_TOLERANCE_POINTS:
                continue

            horizontal_overlap = min(float(left["x1"]), float(right["x1"])) - max(
                float(left["x0"]), float(right["x0"])
            )
            if horizontal_overlap <= OVERLAP_TOLERANCE_POINTS:
                continue

            overlaps.append(
                {
                    "pageNumber": page_number,
                    "horizontalOverlap": round(horizontal_overlap, 2),
                    "verticalOverlap": round(vertical_overlap, 2),
                    "leftTextHash": sha256_text(normalize_text(str(left.get("text") or ""))),
                    "rightTextHash": sha256_text(normalize_text(str(right.get("text") or ""))),
                    "top": round(min(float(left["top"]), float(right["top"])), 2),
                }
            )
    return overlaps


def extract_pdf_layout(pdf_path: Path) -> dict[str, Any]:
    pages: list[dict[str, Any]] = []
    all_wrap_hashes: list[str] = []
    overlaps: list[dict[str, Any]] = []

    with pdfplumber.open(pdf_path) as pdf:
        for page_number, page in enumerate(pdf.pages, start=1):
            words = page.extract_words(
                x_tolerance=1,
                y_tolerance=2,
                keep_blank_chars=False,
                use_text_flow=False,
            )
            lines = group_words_into_lines(words)
            if not lines:
                raise RuntimeError(f"{pdf_path}: page {page_number} contains no extractable text lines.")

            page_overlaps = find_text_overlaps(words, page_number)
            overlaps.extend(page_overlaps)
            line_hashes = [line["textHash"] for line in lines]
            line_wrap_hashes = [line["wrapHash"] for line in lines]
            all_wrap_hashes.extend(line_wrap_hashes)
            pages.append(
                {
                    "pageNumber": page_number,
                    "width": round(float(page.width), 2),
                    "height": round(float(page.height), 2),
                    "wordCount": len(words),
                    "lineCount": len(lines),
                    "lineHashes": line_hashes,
                    "lineWrapHashes": line_wrap_hashes,
                    "lineTops": [line["top"] for line in lines],
                    "lineLefts": [line["left"] for line in lines],
                    "lineRights": [line["right"] for line in lines],
                }
            )

    if overlaps:
        first = overlaps[0]
        raise RuntimeError(
            f"{pdf_path}: detected {len(overlaps)} overlapping text pairs; "
            f"first overlap is on page {first['pageNumber']} at top {first['top']}pt."
        )

    return {
        "schemaVersion": 2,
        "slug": pdf_path.stem,
        "sourceFile": pdf_path.name,
        "operatingSystem": platform.platform(),
        "architecture": platform.machine(),
        "pythonVersion": platform.python_version(),
        "pdfplumberVersion": pdfplumber.__version__,
        "pageCount": len(pages),
        "lineCount": sum(page["lineCount"] for page in pages),
        "layoutHash": sha256_text("\n".join(all_wrap_hashes)),
        "overlapCount": 0,
        "pages": pages,
    }


def main() -> int:
    arguments = parse_arguments()
    root = arguments.root.resolve()
    if root.is_file() and root.suffix.lower() == ".pdf":
        pdf_files = [root]
    elif root.is_dir():
        pdf_files = sorted(root.rglob("*.pdf"))
    else:
        raise FileNotFoundError(f"PDF input does not exist: {root}")

    if not pdf_files:
        raise FileNotFoundError(f"No PDF files were found under {root}")

    for pdf_path in pdf_files:
        layout = extract_pdf_layout(pdf_path)
        output_path = pdf_path.with_suffix(".layout.json")
        output_path.write_text(
            json.dumps(layout, ensure_ascii=False, indent=2) + "\n",
            encoding="utf-8",
        )
        print(
            f"PDF layout verified: {pdf_path.name}, "
            f"pages={layout['pageCount']}, lines={layout['lineCount']}, overlaps=0"
        )
    return 0


if __name__ == "__main__":
    logging.disable(logging.CRITICAL)
    warnings.filterwarnings("ignore")
    try:
        raise SystemExit(main())
    except Exception as error:  # noqa: BLE001 - concise CI failure reporting
        print(f"PDF layout extraction failed: {error}", file=sys.stderr)
        raise SystemExit(1)
