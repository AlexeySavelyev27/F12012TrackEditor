using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Threading.Tasks;

namespace PSSGEditor
{
    public partial class MainWindow
    {
        private async void OpenMenuItem_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "PSSG/.ens files (*.pssg;*.ens)|*.pssg;*.ens",
                Title = "Open PSSG File"
            };
            if (ofd.ShowDialog() != true) return;

            await LoadFileAsync(ofd.FileName);
        }

        public async Task LoadFileAsync(string fileName)
        {
            StatusText.Text = "Loading...";
            try
            {
                var node = await Task.Run(() =>
                {
                    var parser = new PSSGParser(fileName);
                    return parser.Parse();
                });

                rootNode = node;

                var stats = CollectStats(rootNode);
                StatusText.Text = $"Nodes: {stats.nodes}, Meshes: {stats.meshes}, Textures: {stats.textures}";

                PopulateTreeView();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to open file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error opening file";
            }
        }

        private void SaveAsMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (rootNode == null) return;

            var sfd = new SaveFileDialog
            {
                Filter = "PSSG files (*.pssg)|*.pssg",
                Title = "Save as PSSG"
            };
            if (sfd.ShowDialog() != true) return;

            try
            {
                var writer = new PSSGWriter(rootNode);
                writer.Save(sfd.FileName);
                StatusText.Text = $"Saved: {sfd.FileName}";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save file:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error saving file";
            }
        }

        private void ExitMenuItem_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}

