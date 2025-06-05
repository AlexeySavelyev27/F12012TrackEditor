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
        # Эти поля будут рассчитаны при сохранении
        self.attr_block_size = 0
        self.node_size = 0

class PSSGSchema:
    def __init__(self):
        # Локальные соответствия для каждого типа узла
        self.node_id_to_name = {}
        self.attr_id_to_name = {}         # { node_id: { attr_id: attr_name } }
        self.node_name_to_id = {}
        self.attr_name_to_id = {}         # { node_name: { attr_name: attr_id } }
        # Глобальные словари, чтобы один и тот же attr_id всегда давал одно имя
        self.global_attr_id_to_name = {}  # { attr_id: attr_name }
        self.global_attr_name_to_id = {}  # { attr_name: attr_id }

    def build_from_tree(self, root):
        """
        Собирает схему исходя из уже распарсенного дерева узлов.
        Каждому уникальному имени узла даёт свой NodeID.
        Каждому атрибуту в составе узлов даёт свой AttrID.
        """
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

        # Присвоим каждому узлу идентификатор
        for idx, name in enumerate(node_names, start=1):
            self.node_id_to_name[idx] = name
            self.node_name_to_id[name] = idx

        # Присвоим каждому атрибуту идентификатор в рамках конкретного типа узла
        for name, attrs in attr_map.items():
            node_id = self.node_name_to_id[name]
            self.attr_id_to_name[node_id] = {}
            self.attr_name_to_id[name] = {}
            for attr_id, attr_name in enumerate(attrs, start=1):
                self.attr_id_to_name[node_id][attr_id] = attr_name
                self.attr_name_to_id[name][attr_name] = attr_id

        return self

    def _read_schema(self):
        """
        Читает раздел schema из PSSG-файла: для каждого типа узла 
        узнаёт NodeID, имя, количество атрибутов, а затем пары (AttrID, AttrName).
        При этом сразу заполняет и глобальные словари attr ↔ name.
        """
        # Считаем количество информационных записей
        attr_info_count = struct.unpack(BE + 'I', self.buf.read(4))[0]
        node_info_count = struct.unpack(BE + 'I', self.buf.read(4))[0]

        schema = PSSGSchema()
        # Для каждого node type читаем NodeID, Name, AttrCount
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

                # Локальная привязка для конкретного node_id
                schema.attr_id_to_name[node_id][attr_id] = attr_name
                schema.attr_name_to_id[name][attr_name] = attr_id

                # Обновляем глобальные словари, чтобы запомнить имя навсегда
                if attr_id not in schema.global_attr_id_to_name:
                    schema.global_attr_id_to_name[attr_id] = attr_name
                if attr_name not in schema.global_attr_name_to_id:
                    schema.global_attr_name_to_id[attr_name] = attr_id

        return schema

class PSSGParser:
    def __init__(self, path):
        self.path = path

    def parse(self):
        # Считываем файл целиком, если заархивирован GZip — раскодируем
        with open(self.path, 'rb') as f:
            data = f.read()
        if data[:2] == b'\x1f\x8b':
            data = gzip.decompress(data)

        self.buf = io.BytesIO(data)

        # Проверяем сигнатуру "PSSG"
        sig = self.buf.read(4)
        if sig != b'PSSG':
            raise ValueError('Not a PSSG file')

        # Считаем FileDataLength (не используется далее, просто для сдвига указателя)
        _file_data_length = struct.unpack(BE + 'I', self.buf.read(4))[0]

        # Читаем схему (node_id → name, attr_id → name и др.)
        self.schema = self._read_schema()

        # Рекурсивно строим дерево узлов
        root = self._read_node()
        return root

    def _read_schema(self):
        return PSSGSchema._read_schema(self)

    def _read_node(self):
        # Запоминаем начало этого узла
        start = self.buf.tell()

        # Считываем NodeID и NodeSize
        node_id = struct.unpack(BE + 'I', self.buf.read(4))[0]
        node_size = struct.unpack(BE + 'I', self.buf.read(4))[0]
        node_end = self.buf.tell() + node_size

        # Считываем AttrBlockSize
        attr_block_size = struct.unpack(BE + 'I', self.buf.read(4))[0]
        attr_end = self.buf.tell() + attr_block_size

        # Определяем имя узла; если в схеме нет — даём "unknown_<id>"
        name = self.schema.node_id_to_name.get(node_id, f'unknown_{node_id}')

        # Читаем все атрибуты в словарь { attr_name: raw_bytes }
        attrs = {}
        attr_map = self.schema.attr_id_to_name.get(node_id, {})

        while self.buf.tell() < attr_end:
            attr_id = struct.unpack(BE + 'I', self.buf.read(4))[0]
            val_size = struct.unpack(BE + 'I', self.buf.read(4))[0]
            val = self.buf.read(val_size)

            # Если attr_id == 63 — всегда используем имя "id"
            if attr_id == 63:
                attr_name = 'id'
            elif attr_id in attr_map:
                # Если в локальной схеме узла есть имя — берём его
                attr_name = attr_map[attr_id]
            elif attr_id in self.schema.global_attr_id_to_name:
                # Если в глобальной карте уже было имя для этого attr_id — используем то, что сохранили ранее
                attr_name = self.schema.global_attr_id_to_name[attr_id]
            else:
                # Иначе создаём автоматическое имя "attr_<id>"
                attr_name = f'attr_{attr_id}'

            attrs[attr_name] = val

        # После чтения атрибутов читаем дочерние узлы или сырой data
        children = []
        data = b''
        while self.buf.tell() < node_end:
            pos = self.buf.tell()
            remaining = node_end - pos

            # Если остался минимум 8 байт, "заглядываем" это может быть дочерний узел
            if remaining >= 8:
                peek_id = struct.unpack(BE + 'I', self.buf.read(4))[0]
                peek_size = struct.unpack(BE + 'I', self.buf.read(4))[0]

                # Если peek_id есть в схеме и peek_size не превышает границ текущего узла — это вложенный узел
                if (peek_id in self.schema.node_id_to_name) and (peek_size <= (node_end - (pos + 8))):
                    # Откатываем указатель и читаем ребёнка рекурсивно
                    self.buf.seek(pos)
                    child = self._read_node()
                    children.append(child)
                    continue
                else:
                    # Не узел — это сырые данные
                    self.buf.seek(pos)
                    data = self.buf.read(node_end - pos)
                    break
            else:
                # Мало байт, считываем как data
                self.buf.seek(pos)
                data = self.buf.read(node_end - pos)
                break

        # Перемещаем указатель в конец узла
        self.buf.seek(node_end)

        # Если есть дети — data отбрасываем, иначе сохраняем сырые data
        node = PSSGNode(name, attrs, children, data if not children else None)
        return node

class PSSGWriter:
    def __init__(self, root):
        self.root = root
        self.schema = PSSGSchema()
        # Восстанавливаем схему из уже изменённого дерева (присваиваем новые NodeID/AttrID)
        self.schema.build_from_tree(root)

    def _compute_sizes(self, node):
        """
        Рекурсивно вычисляет атрибуты node_size и attr_block_size для каждого узла
        """
        # Размер атрибутов: 4 байта для AttrID + 4 байта для ValueSize + len(value)
        attr_size = sum(8 + len(v) for v in node.attributes.values())
        child_payload = 0

        if node.children:
            for c in node.children:
                self._compute_sizes(c)
                # Для ребенка: 4 байта NodeID + 4 байта NodeSize + собственный node_size
                child_payload += 8 + c.node_size
        else:
            # Если нет детей, payload — это длина raw data
            child_payload = len(node.data or b'')

        node.attr_block_size = attr_size
        # NodeSize = 4 байта (весь размер AttrBlockSize) + размер всех атрибутов + payload ребёнков/данных
        node.node_size = 4 + attr_size + child_payload

    def save(self, path):
        """
        Сохраняет PSSGNode (root) назад в бинарный файл по указанному пути.
        """
        # Сначала вычисляем размеры всех узлов
        self._compute_sizes(self.root)

        with open(path, 'wb') as f:
            # Пишем сигнатуру "PSSG"
            f.write(b'PSSG')
            # Файл данных (может быть 0, т.к. мы перезаписываем всё)
            f.write(struct.pack(BE + 'I', 0))

            # Записываем схему в заголовке
            # Считаем общее количество атрибутов по всем узлам
            attr_entry_count = sum(len(a) for a in self.schema.attr_name_to_id.values())
            node_entry_count = len(self.schema.node_name_to_id)

            f.write(struct.pack(BE + 'I', attr_entry_count))
            f.write(struct.pack(BE + 'I', node_entry_count))

            # Для каждого типа узла: NodeID, имя, длина имени, AttrCount,
            # затем пары (AttrID, длина имени атрибута, имя атрибута)
            for node_name, node_id in self.schema.node_name_to_id.items():
                name_bytes = node_name.encode('utf-8')
                f.write(struct.pack(BE + 'I', node_id))
                f.write(struct.pack(BE + 'I', len(name_bytes)))
                f.write(name_bytes)

                attr_map = self.schema.attr_name_to_id.get(node_name, {})
                f.write(struct.pack(BE + 'I', len(attr_map)))
                for attr_name, attr_id in attr_map.items():
                    attr_name_bytes = attr_name.encode('utf-8')
                    f.write(struct.pack(BE + 'I', attr_id))
                    f.write(struct.pack(BE + 'I', len(attr_name_bytes)))
                    f.write(attr_name_bytes)

            # Рекурсивно пишем сам узел
            self._write_node(f, self.root)

    def _write_node(self, f, node):
        """
        Записывает один узел: NodeID, NodeSize, AttrBlockSize, 
        затем все атрибуты, потом детей или raw data.
        """
        # Пишем NodeID
        node_id = self.schema.node_name_to_id[node.name]
        f.write(struct.pack(BE + 'I', node_id))

        # Пишем NodeSize
        f.write(struct.pack(BE + 'I', node.node_size))

        # Пишем AttrBlockSize
        f.write(struct.pack(BE + 'I', node.attr_block_size))

        # Пишем каждый атрибут: AttrID, ValueSize, Value
        for attr_name, value in node.attributes.items():
            if attr_name == 'id':
                # Если пользователь вручную оставил «id», считаем, что этот идентификатор = 63
                attr_id = 63
            elif attr_name in self.schema.attr_name_to_id.get(node.name, {}):
                # Если имя атрибута есть в локальной схеме этого node.name
                attr_id = self.schema.attr_name_to_id[node.name][attr_name]
            elif attr_name in self.schema.global_attr_name_to_id:
                # Если имя было сохранено глобально (ранее найдено в схеме), используем тот ID
                attr_id = self.schema.global_attr_name_to_id[attr_name]
            elif attr_name.startswith('attr_'):
                try:
                    attr_id = int(attr_name.split('_')[1])
                except ValueError:
                    raise ValueError(f"Unknown attribute name: {attr_name}")
            else:
                raise ValueError(f"Unknown attribute name: {attr_name}")

            f.write(struct.pack(BE + 'I', attr_id))
            f.write(struct.pack(BE + 'I', len(value)))
            f.write(value)

        # Если есть дочерние узлы — пишем их рекурсивно, иначе — сырые данные
        if node.children:
            for c in node.children:
                self._write_node(f, c)
        else:
            if node.data:
                f.write(node.data)
