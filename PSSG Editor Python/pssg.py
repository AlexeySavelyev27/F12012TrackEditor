# pssg.py

import struct
import io
import gzip

BE = '>'  # Big-endian for all multi-byte integers

class PSSGNode:
    def __init__(self, name, attributes=None, children=None, data=None):
        self.name = name
        self.attributes = attributes or {}
        self.children = children or []
        self.data = data
        # These will be computed when saving
        self.attr_block_size = 0
        self.node_size = 0

class PSSGSchema:
    def __init__(self):
        self.node_id_to_name = {}
        self.attr_id_to_name = {}
        self.node_name_to_id = {}
        self.attr_name_to_id = {}

    def build_from_tree(self, root):
        node_names = []
        attr_map = {}

        def collect(node):
            if node.name not in node_names:
                node_names.append(node.name)
            attr_map.setdefault(node.name, [])
            for a in node.attributes:
                if a not in attr_map[node.name]:
                    attr_map[node.name].append(a)
            for c in node.children:
                collect(c)

        collect(root)

        # Assign NodeID in order of first appearance
        for idx, name in enumerate(node_names, start=1):
            self.node_id_to_name[idx] = name
            self.node_name_to_id[name] = idx

        # Assign AttrID for each NodeID
        for name, attrs in attr_map.items():
            node_id = self.node_name_to_id[name]
            self.attr_id_to_name[node_id] = {}
            self.attr_name_to_id[name] = {}
            for a_idx, a_name in enumerate(attrs, start=1):
                self.attr_id_to_name[node_id][a_idx] = a_name
                self.attr_name_to_id[name][a_name] = a_idx

class PSSGParser:
    def __init__(self, path):
        self.path = path

    def parse(self):
        # Read entire file, decompress if GZip
        with open(self.path, 'rb') as f:
            data = f.read()
        if data[:2] == b'\x1f\x8b':
            data = gzip.decompress(data)

        self.buf = io.BytesIO(data)

        # Check signature
        sig = self.buf.read(4)
        if sig != b'PSSG':
            raise ValueError('Not a PSSG file')

        # FileDataLength (we read it just to advance the pointer)
        self.file_data_length = struct.unpack(BE + 'I', self.buf.read(4))[0]

        # Read schema (node_id→name, attr_id→name, etc.)
        self.schema = self._read_schema()

        # Recursively build node tree
        root = self._read_node()
        return root

    def _read_schema(self):
        # Read AttributeInfoCount and NodeInfoCount
        attr_info_count = struct.unpack(BE + 'I', self.buf.read(4))[0]
        node_info_count = struct.unpack(BE + 'I', self.buf.read(4))[0]

        schema = PSSGSchema()
        # For each node type, read NodeID, Name, AttrCount and all its AttrID→AttrName
        for _ in range(node_info_count):
            node_id = struct.unpack(BE + 'I', self.buf.read(4))[0]
            name_len = struct.unpack(BE + 'I', self.buf.read(4))[0]
            name = self.buf.read(name_len).decode('utf-8')

            attr_count = struct.unpack(BE + 'I', self.buf.read(4))[0]

            schema.node_id_to_name[node_id] = name
            schema.node_name_to_id[name] = node_id
            schema.attr_id_to_name[node_id] = {}
            schema.attr_name_to_id[name] = {}

            for _ in range(attr_count):
                attr_id = struct.unpack(BE + 'I', self.buf.read(4))[0]
                attr_name_len = struct.unpack(BE + 'I', self.buf.read(4))[0]
                attr_name = self.buf.read(attr_name_len).decode('utf-8')

                schema.attr_id_to_name[node_id][attr_id] = attr_name
                schema.attr_name_to_id[name][attr_name] = attr_id

        return schema

    def _read_node(self):
        # Remember start of this node
        start = self.buf.tell()

        # Read NodeID and NodeSize
        node_id = struct.unpack(BE + 'I', self.buf.read(4))[0]
        node_size = struct.unpack(BE + 'I', self.buf.read(4))[0]
        node_end = self.buf.tell() + node_size

        # Read AttrBlockSize
        attr_block_size = struct.unpack(BE + 'I', self.buf.read(4))[0]
        attr_end = self.buf.tell() + attr_block_size

        # Resolve node name (or unknown_N if missing)
        name = self.schema.node_id_to_name.get(node_id, f'unknown_{node_id}')

        # Read all attributes into a dict: { attr_name: raw_bytes }
        attrs = {}
        attr_map = self.schema.attr_id_to_name.get(node_id, {})

        while self.buf.tell() < attr_end:
            attr_id = struct.unpack(BE + 'I', self.buf.read(4))[0]
            val_size = struct.unpack(BE + 'I', self.buf.read(4))[0]
            val = self.buf.read(val_size)

            # Custom mapping: if attr_id == 63, force name "id"
            # (In original file objects.pssg this поле обычно соответствует 'id'
            if attr_id == 63:
                attr_name = 'id'
            elif attr_id in attr_map:
                attr_name = attr_map[attr_id]
            else:
                attr_name = f'attr_{attr_id}'

            attrs[attr_name] = val

        # Now read either child nodes or raw data
        children = []
        data = b''
        while self.buf.tell() < node_end:
            pos = self.buf.tell()
            remaining = node_end - pos

            # If at least 8 bytes remain, peek next NodeID and NodeSize
            if remaining >= 8:
                peek_id = struct.unpack(BE + 'I', self.buf.read(4))[0]
                peek_size = struct.unpack(BE + 'I', self.buf.read(4))[0]

                # If peek_id exists in schema and peek_size does not exceed this node's bounds, it's a child
                if (peek_id in self.schema.node_id_to_name) and (peek_size <= (node_end - (pos + 8))):
                    # Move pointer back and read child recursively
                    self.buf.seek(pos)
                    child = self._read_node()
                    children.append(child)
                    continue
                else:
                    # Not a child node; reset pointer so we read raw data
                    self.buf.seek(pos)

            # If not a child, read remaining bytes as raw data
            data = self.buf.read(node_end - self.buf.tell())
            break

        # Ensure buffer is positioned exactly at node_end
        self.buf.seek(node_end)

        # If there are child nodes, data = None; otherwise preserve raw data
        node = PSSGNode(name, attrs, children, data if not children else None)
        return node

class PSSGWriter:
    def __init__(self, root):
        self.root = root
        self.schema = PSSGSchema()
        # Rebuild schema from the tree (assign new NodeID/AttrID)
        self.schema.build_from_tree(root)

    def _compute_sizes(self, node):
        # Compute total size of attribute block
        attr_size = sum(8 + len(v) for v in node.attributes.values())
        child_payload = 0

        if node.children:
            for c in node.children:
                self._compute_sizes(c)
                # 4 bytes for NodeID + 4 bytes for NodeSize + child.node_size
                child_payload += 8 + c.node_size
        else:
            # If no children, payload is raw data length
            child_payload = len(node.data or b'')

        node.attr_block_size = attr_size
        # NodeSize = 4 bytes (AttrBlockSize) + size of attribute block + payload
        node.node_size = 4 + attr_size + child_payload

    def save(self, path):
        # First compute sizes for all nodes
        self._compute_sizes(self.root)

        with open(path, 'wb') as f:
            # Write header
            f.write(b'PSSG')
            f.write(struct.pack(BE + 'I', 0))  # placeholder for FileDataLength

            # Build schema entries
            attr_entry_count = sum(len(a) for a in self.schema.attr_name_to_id.values())
            node_entry_count = len(self.schema.node_name_to_id)

            f.write(struct.pack(BE + 'I', attr_entry_count))
            f.write(struct.pack(BE + 'I', node_entry_count))

            # For each node type: NodeID, name length, name, AttrCount, then each AttrID + name
            for node_name, node_id in self.schema.node_name_to_id.items():
                name_bytes = node_name.encode('utf-8')
                f.write(struct.pack(BE + 'I', node_id))
                f.write(struct.pack(BE + 'I', len(name_bytes)))
                f.write(name_bytes)

                attr_map = self.schema.attr_name_to_id.get(node_name, {})
                f.write(struct.pack(BE + 'I', len(attr_map)))
                for attr_name, attr_id in attr_map.items():
                    attr_bytes = attr_name.encode('utf-8')
                    f.write(struct.pack(BE + 'I', attr_id))
                    f.write(struct.pack(BE + 'I', len(attr_bytes)))
                    f.write(attr_bytes)

            # Recursively write node tree
            self._write_node(f, self.root)

            # Go back and fill FileDataLength = (file_size - 8)
            end = f.tell()
            f.seek(4)
            f.write(struct.pack(BE + 'I', end - 8))

    def _write_node(self, f, node):
        # Write NodeID
        node_id = self.schema.node_name_to_id[node.name]
        f.write(struct.pack(BE + 'I', node_id))

        # Write NodeSize
        f.write(struct.pack(BE + 'I', node.node_size))

        # Write AttrBlockSize
        f.write(struct.pack(BE + 'I', node.attr_block_size))

        # Write each attribute: AttrID, ValueSize, Value
        for attr_name, value in node.attributes.items():
            # If this attribute was originally 'id' (custom), fetch its numeric ID:
            if attr_name == 'id':
                # We assume in schema it was registered under 63 (if building from tree, this may differ)
                attr_id = 63
            else:
                attr_id = self.schema.attr_name_to_id[node.name][attr_name]

            f.write(struct.pack(BE + 'I', attr_id))
            f.write(struct.pack(BE + 'I', len(value)))
            f.write(value)

        # If node has children, write them recursively; else write raw data
        if node.children:
            for c in node.children:
                self._write_node(f, c)
        else:
            if node.data:
                f.write(node.data)
