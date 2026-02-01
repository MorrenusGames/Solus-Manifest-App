using SolusManifestApp.Helpers;
using SolusManifestApp.Models;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace SolusManifestApp.Views.Dialogs
{
    public partial class UpdateEnablerDialog : Window
    {
        public List<SelectableApp> Apps { get; set; }
        public List<SelectableApp> SelectedApps { get; private set; }
        private List<SelectableApp> _allApps;

        public UpdateEnablerDialog(List<SelectableApp> apps)
        {
            InitializeComponent();
            Apps = apps;
            _allApps = apps;
            AppListBox.ItemsSource = Apps;
            SelectedApps = new List<SelectableApp>();
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            var query = SearchBox.Text?.Trim().ToLower() ?? "";
            if (string.IsNullOrEmpty(query))
            {
                AppListBox.ItemsSource = _allApps;
            }
            else
            {
                AppListBox.ItemsSource = _allApps.Where(a =>
                    a.Name.ToLower().Contains(query) ||
                    a.AppId.ToLower().Contains(query)).ToList();
            }
        }

        private void SelectAll_Click(object sender, RoutedEventArgs e)
        {
            var visibleApps = AppListBox.ItemsSource as IEnumerable<SelectableApp> ?? _allApps;
            foreach (var app in visibleApps)
            {
                app.IsSelected = true;
            }
        }

        private void DeselectAll_Click(object sender, RoutedEventArgs e)
        {
            var visibleApps = AppListBox.ItemsSource as IEnumerable<SelectableApp> ?? _allApps;
            foreach (var app in visibleApps)
            {
                app.IsSelected = false;
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            SelectedApps = Apps.Where(a => a.IsSelected).ToList();

            if (SelectedApps.Count == 0)
            {
                MessageBoxHelper.Show("Please select at least one app to enable updates for.",
                    "No Selection",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1)
            {
                DragMove();
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }

    public class SelectableApp : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
    {
        private bool _isSelected;

        public string AppId { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public bool IsUpdateEnabled { get; set; } = false;

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}
