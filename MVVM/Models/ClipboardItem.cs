using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace CenterHubNew.MVVM.Models
{
    public partial class ClipboardItem : ObservableObject
    {
        [ObservableProperty]
        private string _content = string.Empty;

        [ObservableProperty]
        private DateTime _timestamp;

        [ObservableProperty]
        private bool _isPinned;

        [ObservableProperty]
        private string _preview = string.Empty;

        public ClipboardItem()
        {
            Timestamp = DateTime.Now;
        }

        public ClipboardItem(string content) : this()
        {
            Content = content;
            Preview = content.Length > 100 ? content.Substring(0, 100) + "..." : content;
        }
    }
}

