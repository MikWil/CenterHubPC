using CommunityToolkit.Mvvm.ComponentModel;
using System;

namespace CenterHubNew.MVVM.Models
{
    public partial class QuickNote : ObservableObject
    {
        [ObservableProperty]
        private string _id = Guid.NewGuid().ToString();

        [ObservableProperty]
        private string _title = "New Note";

        [ObservableProperty]
        private string _content = string.Empty;

        [ObservableProperty]
        private DateTime _createdAt;

        [ObservableProperty]
        private DateTime _modifiedAt;

        public QuickNote()
        {
            CreatedAt = DateTime.Now;
            ModifiedAt = DateTime.Now;
        }

        public QuickNote(string title, string content = "") : this()
        {
            Title = title;
            Content = content;
        }
    }
}

