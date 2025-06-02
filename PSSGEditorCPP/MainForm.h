#pragma once

using namespace System;
using namespace System::Windows::Forms;

ref class MainForm : public Form {
public:
    MainForm();
private:
    void OnOpen(Object^ sender, EventArgs^ e);
    void OnSave(Object^ sender, EventArgs^ e);
    TreeView^ treeView;
};
