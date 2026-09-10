using System;
using NEXUS.Common;

namespace NEXUS.Models
{
    public class SubTask : ObservableObject
    {
        private string _title = "";
        private bool _isDone;

        public Guid Id { get; set; } = Guid.NewGuid();

        public string Title
        {
            get => _title;
            set => Set(ref _title, value);
        }

        public bool IsDone
        {
            get => _isDone;
            set => Set(ref _isDone, value);
        }
    }
}
