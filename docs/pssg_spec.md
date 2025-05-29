
# PSSG Format Specification (Codemasters EGO Engine)  
## and a Universal Parser

## Overview of the PSSG Format
**PSSG** is a binary resource container (“scene-graph”) used in Codemasters games that run on the **EGO** engine.  
`.pssg` files can embed:

* 3‑D models  
* Animations  
* Textures  
* Materials  
* Shaders & effects  

Technically, PSSG is *binary XML*: a complete XML tree stored in a compact binary form.  
The format originated in **Sony PhyreEngine** and was adopted in the *DiRT*, *GRID*, and *F1* series.

Signature: ASCII "PSSG" (`50 53 53 47`).  
A file is split into a *header* (schema) and the *body* (node tree).

### Why pack everything in one file?
* **Textures**: stored without a DDS header; width/height, pixel format and mip count are attributes, the pixel stream is stored raw & compressed.  
* **Meshes**: vertex/index arrays live in binary child nodes.  
* **Materials & effects**: a set of parameters and references to textures.

---

## File Layout

| Part | Purpose |
|------|---------|
| **A. Header (Schema)** | Declares *node types* & *attributes* |
| **B. Body**            | The actual node tree + binary data |

### A. Schema Header
| # | Field | Size | Description |
|---|-------|------|-------------|
| 1 | `PSSG`          | 4 B | signature |
| 2 | FileSize        | u32 | file length − 8 |
| 3 | **MAX_ATTR_ID** | u32 | number of unique attributes |
| 4 | **NUM_ELEMENTS**| u32 | number of unique element types |
| 5–13 | … | … | repeated for every element type |

**Per element type**

1. `ElementIndex` (u32)  
2. `NameLen` (u32) → *N* bytes of name  
3. `AttrCount` (u32)  
4. For each attribute: `AttrID` + “len + name”

> The special element `"XXX"` lists *global* attributes (usually just `id`).

### B. Node Record (Body)

```text
ElementIndex u32
TotalSize    u32   // size of the block after this field
AttrDataSize u32
[ Attribute records ]   // AttrDataSize bytes
[ Content ]             // TotalSize - 4 - AttrDataSize
```

#### Attribute record
```text
AttrID       u32
AttrValueSz  u32
AttrValue    bytes[..]
```

* **4‑byte value** → usually `int32` or `float`  
  — heuristic: outside ±9.8e8 → treat as `float`.  
* **> 4 bytes**  
  — if the first dword equals `(AttrValueSz − 4)` → ASCII/UTF‑8 string  
  — otherwise treat the remainder as raw bytes (`byte[]`).

#### Node content
* **Child nodes** (recursive tree), or  
* **Raw binary payload** (leaf nodes: texture mips, vertex buffers, …)

EGO 2012 convention:  
* nodes **with attributes** usually contain children;  
* nodes **without attributes** but *with data* are pure binary leaves.

---

## Common Nodes (F1 2012 · Melbourne Track)

| File | Node type | Key attributes | Content |
|------|-----------|----------------|---------|
| `materials.pssg` | `TEXTURE` | `width`, `height`, `texelFormat`, `mipmapCount`, flags, `id` | children `TEXTUREIMAGEBLOCK` → `TEXTUREIMAGEBLOCKDATA` (raw mip chains) |
|  | `MATERIAL` | `id`, `shaderId`/`effect`, texture refs, numeric params | mostly attributes |
| `track.pssg` | `GEOMETRY` | `id`, `vertexCount`, `indexCount`, `materialId` | child blocks for vertex & index data |
| `objects.pssg` | `SCENE NODE` / `OBJECT` | `id`, transform, `geometryId` | hierarchy of sub‑objects or direct `GEOMETRY` child |

---

## Parsing Algorithm

1. **Header**  
   * read signature, `MAX_ATTR_ID`, `NUM_ELEMENTS`  
   * build dictionaries `elementIndex → name`, `attrID → name`
2. **`ReadNode()`** (recursive)  
   * read ElementIndex / TotalSize / AttrDataSize  
   * parse attributes  
   * `contentBytes = TotalSize − 4 − AttrDataSize`  
   * if children: keep reading nodes until `contentBytes` consumed, else read raw block
3. **DOM** → `PssgNode` (name + attributes + children/raw)

> Keep *RawData* untouched so that re‑saving the file reproduces the original bytes.

### Heuristic: int vs float

```csharp
int raw = br.ReadInt32();
object val = (raw < -100_000 || raw > 100_000)
             ? BitConverter.Int32BitsToSingle(raw)
             : raw;
```

### Writing Back

1. **Regenerate the schema** (if new nodes/attrs were added).  
2. Write header.  
3. Recursively serialize each node:  
   * attributes → block + `AttrDataSize`  
   * children/raw → content  
   * back‑patch `TotalSize`.

---

## Minimal Class Example

```csharp
class PSSGNode {
    public string ElementName;
    public Dictionary<string,object> Attributes = new();
    public List<PSSGNode> Children = new();
    public byte[]? Data;
}
```

```csharp
PSSGNode ReadNode(BinaryReader br) {
    uint idx   = br.ReadUInt32();
    uint size  = br.ReadUInt32();
    uint aSize = br.ReadUInt32();
    ...
}
```

A complete template lives in the full PDF (pp. 6‑10).

---

## Practical Uses

* Convert PSSG ↔ XML for convenient editing.  
* Replace textures (decode `RawData` → DDS, edit, re‑encode).  
* Export/import meshes (e.g. PSSG ⇄ glTF).  
* Add or delete resources — the parser auto‑extends the header.

Games load the file as long as:

* all sizes/offsets are correct,  
* `id` references are consistent,  
* the tree structure is valid.

---

## References & Tools

* PhyreEngine docs; early modding forum threads  
* **Ego PSSG Editor**, `pssgConverter.py`, community scripts  
* Real‑world files from *F1 2012*, *GRID 2*, etc.

---

> This guide enables **loss‑less** read/write of every `.pssg` type:  
> textures, materials, geometry, and full scene graphs (e.g. the Melbourne track in F1 2012).
