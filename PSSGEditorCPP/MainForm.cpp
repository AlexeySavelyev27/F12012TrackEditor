#include "MainForm.h"
#include "PSSGParser.h"
#include "PSSGWriter.h"
#include <msclr/marshal_cppstd.h>

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

    treeView = gcnew TreeView();
    treeView->Dock = DockStyle::Fill;
    this->Controls->Add(treeView);
}

void MainForm::OnOpen(Object^ sender, EventArgs^ e) {
    OpenFileDialog^ dlg = gcnew OpenFileDialog();
    dlg->Filter = "PSSG files (*.pssg)|*.pssg|All files (*.*)|*.*";
    if (dlg->ShowDialog() == System::Windows::Forms::DialogResult::OK) {
        PSSGParser parser(msclr::interop::marshal_as<std::string>(dlg->FileName));
        PSSGNode root = parser.Parse();
        treeView->Nodes->Clear();
        treeView->Nodes->Add(gcnew String(root.name.c_str()));
    }
}

void MainForm::OnSave(Object^ sender, EventArgs^ e) {
    SaveFileDialog^ dlg = gcnew SaveFileDialog();
    dlg->Filter = "PSSG files (*.pssg)|*.pssg";
    if (dlg->ShowDialog() == System::Windows::Forms::DialogResult::OK) {
        // TODO: save current tree
    }
}
