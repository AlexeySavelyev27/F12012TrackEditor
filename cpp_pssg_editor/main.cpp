#include <QApplication>
#include <QMainWindow>
#include <QTreeWidget>
#include <QTableWidget>
#include <QSplitter>
#include <QFileDialog>
#include <QMenuBar>
#include <QStatusBar>
#include "PSSGParser.h"
#include "PSSGWriter.h"

class Editor : public QMainWindow {
    Q_OBJECT
public:
    Editor() {
        tree = new QTreeWidget; tree->setHeaderHidden(true);
        table = new QTableWidget; table->setColumnCount(2);
        splitter = new QSplitter;
        splitter->addWidget(tree);
        splitter->addWidget(table);
        setCentralWidget(splitter);
        auto file = menuBar()->addMenu("File");
        file->addAction("Open", this, &Editor::openFile);
        file->addAction("Save As", this, &Editor::saveAs);
        statusBar();
    }
private slots:
    void openFile() {
        auto path = QFileDialog::getOpenFileName(this, "Open PSSG", QString(), "PSSG files (*.pssg *.ens)");
        if(path.isEmpty()) return;
        PSSGParser parser(path.toStdString());
        root = parser.parse();
        tree->clear();
        addNodeItem(nullptr, root);
    }
    void saveAs() {
        if(!root.name.size()) return;
        auto path = QFileDialog::getSaveFileName(this, "Save PSSG", QString(), "PSSG files (*.pssg)");
        if(path.isEmpty()) return;
        PSSGWriter writer(root);
        writer.save(path.toStdString());
    }
private:
    void addNodeItem(QTreeWidgetItem *parent, const PSSGNode &node) {
        auto item = new QTreeWidgetItem(QStringList(node.name.c_str()));
        if(parent) parent->addChild(item); else tree->addTopLevelItem(item);
        for(const auto &c: node.children) addNodeItem(item, c);
    }
    QTreeWidget *tree; QTableWidget *table; QSplitter *splitter; PSSGNode root;
};

#include "main.moc"

int main(int argc, char *argv[]) {
    QApplication app(argc, argv);
    Editor ed; ed.resize(800,600); ed.show();
    return app.exec();
}
