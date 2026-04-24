using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using MicroseismicSync.Models;

namespace MicroseismicSync.ViewModels
{
    public sealed class StoredFilePanelViewModel
    {
        public StoredFilePanelViewModel(string title)
        {
            Title = title;
            Files = new ObservableCollection<StoredStyleFileItem>();
        }

        public string Title { get; private set; }

        public ObservableCollection<StoredStyleFileItem> Files { get; private set; }

        public void SetFiles(IEnumerable<StoredStyleFileItem> items)
        {
            Files.Clear();

            foreach (var item in items.OrderBy(file => file.FileName))
            {
                Files.Add(item);
            }
        }
    }
}
