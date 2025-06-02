#include "MainForm.h"
#include "PSSGParser.h"
#include "PSSGWriter.h"
#include <msclr/marshal_cppstd.h>
#include <vcclr.h>


MainForm::MainForm() {
    this->Text = "PSSG Editor";
    this->Width = 800;
    this->Height = 600;

    MenuStrip^ menu = gcnew MenuStrip();
    ToolStripMenuItem^ fileMenu = gcnew ToolStripMenuItem("File");
    ToolStripMenuItem^ openItem = gcnew ToolStripMenuItem("Open");
    ToolStripMenuItem^ saveItem = gcnew ToolStripMenuItem("Save As");
    openItem->Click += gcnew EventHandler(this, &MainForm::OnOpen);
    saveItem->Click += gcnew EventHandler(this, &MainForm::OnSave);
    fileMenu->DropDownItems->Add(openItem);
    fileMenu->DropDownItems->Add(saveItem);
    menu->Items->Add(fileMenu);
    this->MainMenuStrip = menu;
    this->Controls->Add(menu);

    SplitContainer^ split = gcnew SplitContainer();
    split->Dock = DockStyle::Fill;
    this->Controls->Add(split);

    treeView = gcnew TreeView();
    treeView->Dock = DockStyle::Fill;
    treeView->AfterSelect += gcnew TreeViewEventHandler(this, &MainForm::OnNodeSelect);
    split->Panel1->Controls->Add(treeView);

    gridView = gcnew DataGridView();
    gridView->Dock = DockStyle::Fill;
    gridView->ColumnCount = 2;
    gridView->Columns[0]->Name = "Attribute";
    gridView->Columns[1]->Name = "Value";
    split->Panel2->Controls->Add(gridView);
}

void MainForm::OnOpen(Object^ sender, EventArgs^ e) {
    OpenFileDialog^ dlg = gcnew OpenFileDialog();
    dlg->Filter = "PSSG files (*.pssg)|*.pssg|All files (*.*)|*.*";
    if (dlg->ShowDialog() == System::Windows::Forms::DialogResult::OK) {
        PSSGParser parser(msclr::interop::marshal_as<std::string>(dlg->FileName));
        rootNode_ = parser.Parse();
        treeView->Nodes->Clear();
        TreeNode^ root = gcnew TreeNode(gcnew String(rootNode_.name.c_str()));
        root->Tag = System::IntPtr(&rootNode_);
        treeView->Nodes->Add(root);

        System::Collections::Generic::Queue<System::Tuple<TreeNode^, PSSGNode*>^>^ q =
            gcnew System::Collections::Generic::Queue<System::Tuple<TreeNode^, PSSGNode*>^>();
        q->Enqueue(gcnew System::Tuple<TreeNode^, PSSGNode*>(root, &rootNode_));
        while(q->Count > 0) {
            auto pair = q->Dequeue();
            TreeNode^ n = pair->Item1;
            PSSGNode* nptr = pair->Item2;
            int idx = 0;
            for(auto& c : nptr->children) {
                TreeNode^ cn = gcnew TreeNode(gcnew String(c.name.c_str()));
                cn->Tag = System::IntPtr(&nptr->children[idx]);
                n->Nodes->Add(cn);
                q->Enqueue(gcnew System::Tuple<TreeNode^, PSSGNode*>(cn, &nptr->children[idx]));
                ++idx;
            }
        }
        treeView->ExpandAll();
        treeView->SelectedNode = root;
    }
}

void MainForm::OnSave(Object^ sender, EventArgs^ e) {
    SaveFileDialog^ dlg = gcnew SaveFileDialog();
    dlg->Filter = "PSSG files (*.pssg)|*.pssg";
    if (dlg->ShowDialog() == System::Windows::Forms::DialogResult::OK) {
        PSSGWriter writer(rootNode_);
        writer.Save(msclr::interop::marshal_as<std::string>(dlg->FileName));
    }
}

void MainForm::OnNodeSelect(Object^ sender, TreeViewEventArgs^ e) {
    gridView->Rows->Clear();
    if(!e->Node) return;
    PSSGNode* node = reinterpret_cast<PSSGNode*>(e->Node->Tag.ToPointer());
    if(!node) return;
    for(const auto& kv : node->attributes) {
        String^ n = gcnew String(kv.first.c_str());
        System::Text::StringBuilder^ sb = gcnew System::Text::StringBuilder();
        for(unsigned char b : kv.second) sb->AppendFormat("{0:X2}", b);
        gridView->Rows->Add(n, sb->ToString());
    }
    if(!node->children.empty()) return;
    if(!node->data.empty()) {
        System::Text::StringBuilder^ sb = gcnew System::Text::StringBuilder();
        for(unsigned char b : node->data) sb->AppendFormat("{0:X2}", b);
        gridView->Rows->Add("DATA", sb->ToString());
    }
}
