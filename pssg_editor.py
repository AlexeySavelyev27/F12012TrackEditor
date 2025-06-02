import tkinter as tk
from tkinter import ttk, filedialog, messagebox
from pssg import PSSGParser, PSSGWriter, PSSGNode
import struct

class Editor(tk.Tk):
    def __init__(self):
        super().__init__()
        self.title('PSSG Editor')
        self.geometry('900x600')
        self._build_menu()

        # Разделитель панелей: позволяет менять ширину левой и правой части
        # Сделаем разделитель заметным, как в Total Commander
        self.paned = tk.PanedWindow(
            self,
            orient='horizontal',
            sashwidth=8,
            sashrelief='raised'
        )
        self.paned.pack(fill='both', expand=True)

        # --- Левая панель: дерево нодов без обводки ---
        tree_frame = tk.Frame(self.paned)
        # Сам Treeview тоже без рамки
        self.tree = ttk.Treeview(tree_frame, show='tree')
        self.tree_scroll = ttk.Scrollbar(tree_frame, orient='vertical', command=self.tree.yview)
        self.tree.configure(yscrollcommand=self._tree_scroll)
        self.tree.pack(side='left', fill='both', expand=True)
        self.tree_scroll.pack(side='left', fill='y')
        self.tree.bind('<<TreeviewSelect>>', self._on_select)
        self.tree.bind('<Configure>', lambda e: self._update_tree_scrollbar())
        self.paned.add(tree_frame)

        # --- Правая панель: серый фон, без рамки ---
        self.right_frame = tk.Frame(self.paned, bg='#e0e0e0', bd=0, highlightthickness=0)
        self.attr_frame = tk.Frame(self.right_frame, bg='#e0e0e0')
        self.attr_frame.pack(fill='both', expand=True)
        self.paned.add(self.right_frame)

        # Разделитель над строкой состояния
        separator = ttk.Separator(self, orient='horizontal')
        separator.pack(fill='x')

        # --- Строка состояния снизу ---
        self.status_var = tk.StringVar()
        self.status = tk.Label(self, textvariable=self.status_var, anchor='w')
        self.status.pack(fill='x', side='bottom')
        self.status_var.set('Ready')

        # Для inline-редактирования
        self.selected_label = None
        self.selected_label_bg = '#cce5ff'  # стандартный синий оттенок при выделении
        self.line_editing_info = None  # (номер_строки, ссылка_на_лейбл)
        self.editing_entry = None

        # Хранилище узлов и текущих отображённых данных
        self.node_items = {}         # map item_id -> PSSGNode
        self.current_node = None     # текущий выбранный узел
        self.current_mappings = {}   # map row_index -> (attr_name, byte_length, ссылка_на_лейбл)
        self.root_node = None

    def _build_menu(self):
        menubar = tk.Menu(self)
        file_menu = tk.Menu(menubar, tearoff=0)
        file_menu.add_command(label='Open', command=self.open_file)
        file_menu.add_command(label='Save As', command=self.save_as)
        file_menu.add_separator()
        file_menu.add_command(label='Exit', command=self.destroy)
        menubar.add_cascade(label='File', menu=file_menu)
        self.config(menu=menubar)

    def _tree_scroll(self, lo, hi):
        self.tree_scroll.set(lo, hi)
        self._update_tree_scrollbar()

    def _update_tree_scrollbar(self):
        lo, hi = self.tree.yview()
        if hi - lo >= 1.0:
            if self.tree_scroll.winfo_ismapped():
                self.tree_scroll.pack_forget()
        else:
            if not self.tree_scroll.winfo_ismapped():
                self.tree_scroll.pack(side='left', fill='y')

    def _collect_stats(self, root):
        nodes = meshes = textures = 0
        stack = [root]
        while stack:
            n = stack.pop()
            nodes += 1
            if n.name.upper() == 'MESH':
                meshes += 1
            if n.name.upper() == 'TEXTURE':
                textures += 1
            stack.extend(n.children)
        return nodes, meshes, textures

    def open_file(self):
        path = filedialog.askopenfilename(filetypes=[('PSSG files', '*.pssg *.ens')])
        if not path:
            return
        try:
            parser = PSSGParser(path)
            self.root_node = parser.parse()
            n_nodes, n_meshes, n_textures = self._collect_stats(self.root_node)
            self.status_var.set(f"Nodes: {n_nodes}, Meshes: {n_meshes}, Textures: {n_textures}")
        except Exception as e:
            messagebox.showerror('Error', f'Failed to open: {e}')
            self.status_var.set('Failed to open file')
            return
        self._populate_tree()

    def _populate_tree(self):
        self.tree.delete(*self.tree.get_children())
        self.node_items.clear()

        def recurse(parent, node):
            item = self.tree.insert(parent, 'end', text=node.name)
            self.node_items[item] = node
            for child in node.children:
                recurse(item, child)

        if self.root_node:
            recurse('', self.root_node)
            first = self.tree.get_children()
            if first:
                self.tree.selection_set(first[0])
        self.tree.update_idletasks()
        self._update_tree_scrollbar()

    def _on_select(self, event):
        if self.editing_entry:
            self._finish_inline_edit(None)
        sel = self.tree.selection()
        if not sel:
            return
        item = sel[0]
        node = self.node_items.get(item)
        if not node:
            return
        self.current_node = node

        # Очищаем правую панель
        for w in self.attr_frame.winfo_children():
            w.destroy()
        self.current_mappings.clear()
        self.selected_label = None

        # Если только DATA
        if not node.attributes and node.data is not None:
            self._show_data_only(node)
            return
        # Если нет атрибутов и нет DATA
        if not node.attributes and node.data is None:
            return
        # Иначе – таблица атрибутов + DATA
        self._show_attributes_table(node)

    def _show_data_only(self, node):
        data_text = self._bytes_to_display('__data__', node.data)
        text = tk.Text(self.attr_frame, wrap='word', bg='#ffffff')
        text.insert('1.0', data_text)
        text.configure(state='normal')
        text.pack(fill='both', expand=True, padx=10, pady=10)

    def _show_attributes_table(self, node):
        header_bg = '#d0d0d0'  # цвет заголовков столбцов
        col1_bg = '#dcdcdc'    # цвет полей столбца Attributes
        col2_bg = '#ffffff'
        fg = '#000000'

        # Заголовок
        lbl_attr_h = tk.Label(self.attr_frame, text='Attributes', bg=header_bg, fg=fg, relief='ridge', bd=1)
        lbl_attr_h.grid(row=0, column=0, sticky='nsew')
        lbl_val_h = tk.Label(self.attr_frame, text='Values', bg=header_bg, fg=fg, relief='ridge', bd=1)
        lbl_val_h.grid(row=0, column=1, sticky='nsew')

        # Растягиваем столбцы
        self.attr_frame.columnconfigure(0, weight=1)
        self.attr_frame.columnconfigure(1, weight=2)

        # Заполнение строк
        row = 1
        for name, raw in node.attributes.items():
            display = self._bytes_to_display(name, raw)
            lbl_attr = tk.Label(self.attr_frame, text=name, bg=col1_bg, fg=fg, relief='ridge', bd=1)
            lbl_attr.grid(row=row, column=0, sticky='nsew')
            lbl_val = tk.Label(self.attr_frame, text=display, bg=col2_bg, fg=fg,
                               relief='ridge', bd=1, anchor='w')
            lbl_val.grid(row=row, column=1, sticky='nsew')

            lbl_val.bind('<Button-1>', lambda e, lab=lbl_val: self._on_value_click(e, lab))
            lbl_val.bind('<Double-1>', lambda e, r=row: self._start_inline_edit(r))

            self.current_mappings[row] = (name, len(raw), lbl_val)
            row += 1

        if node.data is not None:
            display = self._bytes_to_display('__data__', node.data)
            lbl_attr = tk.Label(self.attr_frame, text='DATA', bg=col1_bg, fg=fg, relief='ridge', bd=1)
            lbl_attr.grid(row=row, column=0, sticky='nsew')
            lbl_val = tk.Label(self.attr_frame, text=display, bg=col2_bg, fg=fg,
                               relief='ridge', bd=1, anchor='w')
            lbl_val.grid(row=row, column=1, sticky='nsew')

            lbl_val.bind('<Button-1>', lambda e, lab=lbl_val: self._on_value_click(e, lab))
            lbl_val.bind('<Double-1>', lambda e, r=row: self._start_inline_edit(r))

            self.current_mappings[row] = ('__data__', len(node.data), lbl_val)
            row += 1

    def _on_value_click(self, event, label):
        if self.selected_label and self.selected_label != label:
            self.selected_label.configure(bg='#ffffff')
        label.configure(bg=self.selected_label_bg)
        self.selected_label = label

    def _start_inline_edit(self, row):
        if self.editing_entry:
            self._finish_inline_edit(None)
        info = self.current_mappings.get(row)
        if not info:
            return
        name, length, lbl_val = info
        x = lbl_val.winfo_x()
        y = lbl_val.winfo_y()
        w = lbl_val.winfo_width()
        h = lbl_val.winfo_height()
        value = lbl_val.cget('text')
        entry = tk.Entry(self.attr_frame)
        entry.insert(0, value)
        entry.select_range(0, 'end')
        entry.place(x=x, y=y, width=w, height=h)
        entry.focus()
        self.editing_entry = entry
        self.line_editing_info = (row, lbl_val)
        entry.bind('<Return>', self._finish_inline_edit)
        entry.bind('<FocusOut>', self._finish_inline_edit)

    def _finish_inline_edit(self, event):
        if not self.editing_entry:
            return
        entry = self.editing_entry
        new_text = entry.get()
        row, lbl_val = self.line_editing_info
        name, length, _ = self.current_mappings.get(row)
        if name == '__data__':
            self.current_node.data = self._display_to_bytes(name, new_text, length)
        else:
            self.current_node.attributes[name] = self._display_to_bytes(name, new_text, length)
        lbl_val.configure(text=new_text)
        entry.destroy()
        self.editing_entry = None
        self.line_editing_info = None

    def _bytes_to_display(self, name, b):
        if len(b) >= 4:
            sz = struct.unpack('>I', b[:4])[0]
            if sz <= len(b) - 4:
                try:
                    return b[4:4+sz].decode('utf-8')
                except Exception:
                    pass
        try:
            txt = b.decode('utf-8')
            if all(32 <= ord(c) < 127 for c in txt):
                return txt
        except Exception:
            pass
        if name in ("Transform", "BoundingBox") and len(b) % 4 == 0:
            cnt = len(b) // 4
            vals = struct.unpack('>' + 'f'*cnt, b)
            return "\n".join(f"{v:.6f}" for v in vals)
        if len(b) == 1:
            return str(struct.unpack('>B', b)[0])
        if len(b) == 2:
            return str(struct.unpack('>H', b)[0])
        if len(b) == 4:
            return str(struct.unpack('>I', b)[0])
        return b.hex()

    def _display_to_bytes(self, name, s, length=None):
        if s.isdigit():
            try:
                num = int(s)
                if length == 1:
                    return struct.pack('>B', num)
                if length == 2:
                    return struct.pack('>H', num)
                return struct.pack('>I', num)
            except Exception:
                pass
        if ((s.lower().startswith('0x') and all(c in '0123456789abcdefABCDEF' for c in s[2:]) and len(s[2:]) % 2 == 0) or
            (not s.isdigit() and all(c in '0123456789abcdefABCDEF' for c in s) and len(s) % 2 == 0)):
            try:
                hex_str = s[2:] if s.lower().startswith('0x') else s
                return bytes.fromhex(hex_str)
            except Exception:
                pass
        if name in ("Transform", "BoundingBox"):
            try:
                vals = [float(v) for v in s.replace(',', ' ').split()]
                return struct.pack('>' + 'f'*len(vals), *vals)
            except Exception:
                pass
        b = s.encode('utf-8')
        return struct.pack('>I', len(b)) + b

    def save_as(self):
        if not self.root_node:
            return
        path = filedialog.asksaveasfilename(defaultextension='.pssg', filetypes=[('PSSG files','*.pssg')])
        if not path:
            return
        try:
            writer = PSSGWriter(self.root_node)
            writer.save(path)
            self.status_var.set(f"Saved: {path}")
        except Exception as e:
            messagebox.showerror('Error', f'Failed to save: {e}')
            self.status_var.set('Failed to save file')

if __name__ == '__main__':
    Editor().mainloop()
