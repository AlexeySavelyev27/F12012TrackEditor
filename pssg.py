class PSSGNode:
    def __init__(self, name, attributes=None, children=None, data=None):
        self.name = name
        self.attributes = attributes or {}
        self.children = children or []
        self.data = data
        # computed on save
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
        for idx, name in enumerate(node_names, start=1):
            self.node_id_to_name[idx] = name
            self.node_name_to_id[name] = idx
        for name, attrs in attr_map.items():
            self.attr_id_to_name[self.node_name_to_id[name]] = {}
            self.attr_name_to_id[name] = {}
            for a_idx, a_name in enumerate(attrs, start=1):
                node_id = self.node_name_to_id[name]
                self.attr_id_to_name[node_id][a_idx] = a_name
                self.attr_name_to_id[name][a_name] = a_idx

import struct, io, gzip

BE = '>'

class PSSGParser:
    def __init__(self, path):
        self.path = path

    def parse(self):
        with open(self.path, 'rb') as f:
            data = f.read()
        if data[:2] == b'\x1f\x8b':
            data = gzip.decompress(data)
        self.buf = io.BytesIO(data)
        sig = self.buf.read(4)
        if sig != b'PSSG':
            raise ValueError('Not a PSSG file')
        self.file_data_length = struct.unpack(BE+'I', self.buf.read(4))[0]
        self.schema = self._read_schema()
        root = self._read_node()
        return root

    def _read_schema(self):
        attr_info_count = struct.unpack(BE+'I', self.buf.read(4))[0]
        node_info_count = struct.unpack(BE+'I', self.buf.read(4))[0]
        schema = PSSGSchema()
        for _ in range(node_info_count):
            node_id = struct.unpack(BE+'I', self.buf.read(4))[0]
            name_len = struct.unpack(BE+'I', self.buf.read(4))[0]
            name = self.buf.read(name_len).decode('utf-8')
            attr_count = struct.unpack(BE+'I', self.buf.read(4))[0]
            schema.node_id_to_name[node_id] = name
            schema.node_name_to_id[name] = node_id
            schema.attr_id_to_name[node_id] = {}
            schema.attr_name_to_id[name] = {}
            for _ in range(attr_count):
                attr_id = struct.unpack(BE+'I', self.buf.read(4))[0]
                attr_name_len = struct.unpack(BE+'I', self.buf.read(4))[0]
                attr_name = self.buf.read(attr_name_len).decode('utf-8')
                schema.attr_id_to_name[node_id][attr_id] = attr_name
                schema.attr_name_to_id[name][attr_name] = attr_id
        return schema

    def _read_node(self):
        start = self.buf.tell()
        node_id = struct.unpack(BE+'I', self.buf.read(4))[0]
        node_size = struct.unpack(BE+'I', self.buf.read(4))[0]
        node_end = self.buf.tell() + node_size
        attr_block_size = struct.unpack(BE+'I', self.buf.read(4))[0]
        attr_end = self.buf.tell() + attr_block_size
        name = self.schema.node_id_to_name.get(node_id, f'unknown_{node_id}')
        attrs = {}
        attr_map = self.schema.attr_id_to_name.get(node_id, {})
        while self.buf.tell() < attr_end:
            attr_id = struct.unpack(BE+'I', self.buf.read(4))[0]
            val_size = struct.unpack(BE+'I', self.buf.read(4))[0]
            val = self.buf.read(val_size)
            attr_name = attr_map.get(attr_id, f'attr_{attr_id}')
            attrs[attr_name] = val
        children = []
        data = b''
        while self.buf.tell() < node_end:
            pos = self.buf.tell()
            if node_end - pos >= 12:
                peek = struct.unpack(BE+'I', self.buf.read(4))[0]
                if peek in self.schema.node_id_to_name:
                    self.buf.seek(-4, io.SEEK_CUR)
                    child = self._read_node()
                    children.append(child)
                    continue
                else:
                    self.buf.seek(-4, io.SEEK_CUR)
            data = self.buf.read(node_end - self.buf.tell())
            break
        self.buf.seek(node_end)
        node = PSSGNode(name, attrs, children, data if children==[] else None)
        return node

class PSSGWriter:
    def __init__(self, root):
        self.root = root
        self.schema = PSSGSchema()
        self.schema.build_from_tree(root)

    def _compute_sizes(self, node):
        attr_size = sum(8 + len(v) for v in node.attributes.values())
        child_payload = 0
        if node.children:
            for c in node.children:
                self._compute_sizes(c)
                child_payload += 8 + c.node_size
        else:
            child_payload = len(node.data or b'')
        node.attr_block_size = attr_size
        node.node_size = 4 + attr_size + child_payload

    def save(self, path):
        self._compute_sizes(self.root)
        with open(path, 'wb') as f:
            f.write(b'PSSG')
            f.write(struct.pack(BE+'I', 0))  # placeholder length
            # schema
            attr_entry_count = sum(len(a) for a in self.schema.attr_name_to_id.values())
            node_entry_count = len(self.schema.node_name_to_id)
            f.write(struct.pack(BE+'I', attr_entry_count))
            f.write(struct.pack(BE+'I', node_entry_count))
            for node_name, node_id in self.schema.node_name_to_id.items():
                name_bytes = node_name.encode('utf-8')
                f.write(struct.pack(BE+'I', node_id))
                f.write(struct.pack(BE+'I', len(name_bytes)))
                f.write(name_bytes)
                attr_map = self.schema.attr_name_to_id.get(node_name, {})
                f.write(struct.pack(BE+'I', len(attr_map)))
                for attr_name, attr_id in attr_map.items():
                    attr_bytes = attr_name.encode('utf-8')
                    f.write(struct.pack(BE+'I', attr_id))
                    f.write(struct.pack(BE+'I', len(attr_bytes)))
                    f.write(attr_bytes)
            self._write_node(f, self.root)
            end = f.tell()
            f.seek(4)
            f.write(struct.pack(BE+'I', end - 8))

    def _write_node(self, f, node):
        node_id = self.schema.node_name_to_id[node.name]
        f.write(struct.pack(BE+'I', node_id))
        f.write(struct.pack(BE+'I', node.node_size))
        f.write(struct.pack(BE+'I', node.attr_block_size))
        for attr_name, value in node.attributes.items():
            attr_id = self.schema.attr_name_to_id[node.name][attr_name]
            f.write(struct.pack(BE+'I', attr_id))
            f.write(struct.pack(BE+'I', len(value)))
            f.write(value)
        if node.children:
            for c in node.children:
                self._write_node(f, c)
        else:
            if node.data:
                f.write(node.data)
