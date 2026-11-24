using System.Collections.ObjectModel;
using System.Collections.Specialized;
using mk.dev.runner.core.Logs;

namespace mk.dev.runner.desktop.ViewModels
{
    public sealed class LogLines : ObservableCollection<LogLine>
    {
        public void AddMany(List<LogLine> lines)
        {
            if (lines.Count < 50)
            {
                foreach (var l in lines) Add(l);
                return;
            }
            CheckReentrancy();
            ((List<LogLine>)Items).AddRange(lines);
            Changed();
        }

        public void RemoveFirst(int count)
        {
            if (count <= 0)
                return;
            CheckReentrancy();
            var list = (List<LogLine>)Items;
            list.RemoveRange(0, Math.Min(count, list.Count));
            Changed();
        }

        private void Changed()
        {
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(Count)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs("Item[]"));
            OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
        }
    }
}
