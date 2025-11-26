using SolusManifestApp.ViewModels;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SolusManifestApp.Views
{
    public partial class LibraryPage : UserControl
    {
        private Brush? _originalBackground;

        public LibraryPage()
        {
            InitializeComponent();
        }

        private bool HasValidFilesOrFolders(string[] paths)
        {
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    if (path.EndsWith(".lua", System.StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
                else if (Directory.Exists(path))
                {
                    return true;
                }
            }
            return false;
        }

        private List<string> GetValidFilesFromPaths(string[] paths)
        {
            var validFiles = new List<string>();
            foreach (var path in paths)
            {
                if (File.Exists(path))
                {
                    if (path.EndsWith(".lua", System.StringComparison.OrdinalIgnoreCase) ||
                        path.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase))
                    {
                        validFiles.Add(path);
                    }
                }
                else if (Directory.Exists(path))
                {
                    var filesInFolder = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories)
                        .Where(f => f.EndsWith(".lua", System.StringComparison.OrdinalIgnoreCase) ||
                                    f.EndsWith(".zip", System.StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    validFiles.AddRange(filesInFolder);
                }
            }
            return validFiles;
        }

        private void Grid_DragEnter(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (HasValidFilesOrFolders(files))
                {
                    e.Effects = DragDropEffects.Copy;

                    if (sender is Grid grid)
                    {
                        _originalBackground = grid.Background;
                        grid.Background = new SolidColorBrush(Color.FromArgb(40, 74, 144, 226));
                    }
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            e.Handled = true;
        }

        private void Grid_DragLeave(object sender, DragEventArgs e)
        {
            if (sender is Grid grid && _originalBackground != null)
            {
                grid.Background = _originalBackground;
            }
        }

        private void Grid_Drop(object sender, DragEventArgs e)
        {
            if (sender is Grid grid && _originalBackground != null)
            {
                grid.Background = _originalBackground;
            }

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var droppedPaths = (string[])e.Data.GetData(DataFormats.FileDrop);
                var validFiles = GetValidFilesFromPaths(droppedPaths);

                if (validFiles.Count > 0 && DataContext is LibraryViewModel viewModel &&
                    viewModel.ProcessDroppedFilesCommand.CanExecute(validFiles.ToArray()))
                {
                    viewModel.ProcessDroppedFilesCommand.Execute(validFiles.ToArray());
                }
            }
            e.Handled = true;
        }
    }
}
