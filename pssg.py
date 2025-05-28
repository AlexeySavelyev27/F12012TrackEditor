# Simple module to read, modify and write PSSG archives

from __future__ import annotations

import struct
from dataclasses import dataclass, field
from pathlib import Path
from typing import BinaryIO, List


@dataclass
class PSSGNode:
    """Node element inside a PSSG archive."""

    name: str
    flags: int = 0
    children: List["PSSGNode"] = field(default_factory=list)

    def add_child(self, node: "PSSGNode") -> None:
        self.children.append(node)

    def walk(self) -> List["PSSGNode"]:
        result = [self]
        for child in self.children:
            result.extend(child.walk())
        return result


def _read_cstring(f: BinaryIO) -> str:
    chunks = []
    while True:
        b = f.read(1)
        if not b or b == b"\x00":
            break
        chunks.append(b)
    return b"".join(chunks).decode("utf-8", errors="replace")


def _read_node(f: BinaryIO, limit: int | None = None) -> PSSGNode | None:
    start = f.tell()
    if limit is not None and start >= limit:
        return None

    name_len_data = f.read(4)
    if len(name_len_data) < 4:
        return None
    name_len = struct.unpack(">I", name_len_data)[0]
    name_bytes = f.read(name_len)
    if len(name_bytes) < name_len:
        return None
    name = name_bytes.decode("ascii", errors="replace")
    header = f.read(8)
    if len(header) < 8:
        return None
    flags, child_count = struct.unpack(">II", header)
    node = PSSGNode(name, flags)
    for _ in range(child_count):
        child = _read_node(f, limit)
        if child is not None:
            node.children.append(child)
    return node


def _encode_node(node: PSSGNode) -> bytes:
    parts = [
        struct.pack(">I", len(node.name)),
        node.name.encode("ascii"),
        struct.pack(">II", node.flags, len(node.children)),
    ]
    for child in node.children:
        parts.append(_encode_node(child))
    return b"".join(parts)


def _read_string_table(f: BinaryIO) -> List[str]:
    strings = []
    buf: List[int] = []
    while True:
        b = f.read(1)
        if not b:
            if buf:
                strings.append(bytes(buf).decode("utf-8", errors="replace"))
            break
        if b == b"\x00":
            strings.append(bytes(buf).decode("utf-8", errors="replace"))
            buf = []
        else:
            buf.append(b[0])
    return strings


def _encode_string_table(strings: List[str]) -> bytes:
    parts = []
    for s in strings:
        parts.append(s.encode("utf-8") + b"\x00")
    return b"".join(parts)


@dataclass
class PSSGArchive:
    size: int
    string_table_offset: int
    root_offset: int
    root: PSSGNode
    strings: List[str] = field(default_factory=list)

    @classmethod
    def load(cls, path: Path) -> "PSSGArchive":
        with path.open("rb") as f:
            if f.read(4) != b"PSSG":
                raise ValueError("Invalid PSSG signature")
            size, str_off, root_off = struct.unpack(">III", f.read(12))
            f.seek(root_off)
            root = _read_node(f, str_off)
            f.seek(str_off)
            strings = _read_string_table(f)
        return cls(size, str_off, root_off, root, strings)

    def save(self, path: Path) -> None:
        node_blob = _encode_node(self.root)
        root_off = 0x10  # header size
        str_off = root_off + len(node_blob)
        str_blob = _encode_string_table(self.strings)
        size = str_off + len(str_blob)
        with path.open("wb") as f:
            f.write(b"PSSG")
            f.write(struct.pack(">III", size, str_off, root_off))
            f.write(node_blob)
            f.write(str_blob)
        self.size = size
        self.string_table_offset = str_off
        self.root_offset = root_off
