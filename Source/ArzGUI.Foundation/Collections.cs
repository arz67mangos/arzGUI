using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace CodectoryCore
{
    public static class VersionExtension
    {
        public static Version ApplicationVersion(System.Reflection.Assembly assembly)
        {
            return assembly.GetName().Version;
        }
    }

    public class DispatchingObservableCollection<T> : ObservableCollection<T>
    {
        public DispatchingObservableCollection()
        {
        }

        public DispatchingObservableCollection(IEnumerable<T> items) : base(items)
        {
        }

        protected override void InsertItem(int index, T item)
        {
            Dispatch(() => base.InsertItem(index, item));
        }

        protected override void RemoveItem(int index)
        {
            Dispatch(() => base.RemoveItem(index));
        }

        protected override void SetItem(int index, T item)
        {
            Dispatch(() => base.SetItem(index, item));
        }

        protected override void ClearItems()
        {
            Dispatch(base.ClearItems);
        }

        private static void Dispatch(Action action)
        {
            if (Application.Current == null || Application.Current.Dispatcher.CheckAccess())
            {
                action();
                return;
            }
            // BeginInvoke, not Invoke: callers add to these collections from the watcher thread while
            // holding their own lock (LogsStorage does), and a blocking Invoke then deadlocks against
            // the UI thread waiting for that same lock. Startup hung exactly there.
            Application.Current.Dispatcher.BeginInvoke(action);
        }
    }

    public class SortableObservableCollection<T> : DispatchingObservableCollection<T>
    {
        public SortableObservableCollection()
        {
        }

        public SortableObservableCollection(IEnumerable<T> items) : base(items)
        {
        }

        public void Sort<TKey>(Func<T, TKey> selector, ListSortDirection direction)
        {
            IOrderedEnumerable<T> sorted = direction == ListSortDirection.Ascending
                ? this.OrderBy(selector)
                : this.OrderByDescending(selector);
            ApplyOrder(sorted.ToList());
        }

        public void Sort<TKey>(Func<T, TKey> selector, IComparer<TKey> comparer)
        {
            ApplyOrder(this.OrderBy(selector, comparer).ToList());
        }

        private void ApplyOrder(IList<T> sorted)
        {
            for (int target = 0; target < sorted.Count; target++)
            {
                int current = IndexOf(sorted[target]);
                if (current != target)
                    Move(current, target);
            }
        }
    }
}
