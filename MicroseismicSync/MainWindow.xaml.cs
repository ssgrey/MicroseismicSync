using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using MicroseismicSync.Models;
using MicroseismicSync.ViewModels;

namespace MicroseismicSync
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainViewModel boundViewModel;

        public MainWindow()
        {
            InitializeComponent();
            ConfigureLogDocument();
        }

        private void Window_OnLoaded(object sender, RoutedEventArgs e)
        {
            AttachViewModel(DataContext as MainViewModel);
        }

        private void Window_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            AttachViewModel(e.NewValue as MainViewModel);
        }

        private void Window_OnClosed(object sender, EventArgs e)
        {
            AttachViewModel(null);

            var viewModel = DataContext as MainViewModel;
            if (viewModel != null)
            {
                viewModel.Dispose();
            }
        }

        private void AttachViewModel(MainViewModel viewModel)
        {
            if (ReferenceEquals(boundViewModel, viewModel))
            {
                return;
            }

            if (boundViewModel != null)
            {
                boundViewModel.LogEntriesUpdated -= OnLogEntriesUpdated;
            }

            boundViewModel = viewModel;

            if (boundViewModel != null)
            {
                boundViewModel.LogEntriesUpdated += OnLogEntriesUpdated;
            }

            RefreshLogDocument();
        }

        private void OnLogEntriesUpdated(object sender, EventArgs e)
        {
            RefreshLogDocument();
        }

        private void ConfigureLogDocument()
        {
            LogRichTextBox.Document = CreateLogDocument();
        }

        private void RefreshLogDocument()
        {
            if (LogRichTextBox == null)
            {
                return;
            }

            var document = CreateLogDocument();
            var entries = boundViewModel == null ? null : boundViewModel.GetLogEntriesSnapshot();
            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    document.Blocks.Add(new Paragraph(new Run(entry)) { Margin = new Thickness(0) });
                }
            }

            LogRichTextBox.Document = document;
            LogRichTextBox.ScrollToEnd();
        }

        private static FlowDocument CreateLogDocument()
        {
            return new FlowDocument
            {
                PagePadding = new Thickness(0),
            };
        }

        private async void ManualSyncSelectedMenuItem_OnClick(object sender, RoutedEventArgs e)
        {
            var menuItem = sender as MenuItem;
            var contextMenu = menuItem == null ? null : menuItem.Parent as ContextMenu;
            var dataGrid = contextMenu == null ? null : contextMenu.PlacementTarget as DataGrid;
            var viewModel = DataContext as MainViewModel;
            var fileType = menuItem == null ? null : menuItem.Tag as string;

            if (dataGrid == null || viewModel == null || string.IsNullOrWhiteSpace(fileType))
            {
                return;
            }

            var selectedFiles = dataGrid.SelectedItems
                .OfType<MonitoredFileItem>()
                .ToList();

            await viewModel.SyncSelectedFilesAsync(fileType, selectedFiles);
        }
    }
}
