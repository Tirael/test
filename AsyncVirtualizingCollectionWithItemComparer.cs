using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace zzz.UI.Journals.Infrastructure.DataAccess.DataVirtualization
{
    // ReSharper disable ConvertClosureToMethodGroup
    public class AsyncVirtualizingCollectionWithItemComparer<T> : AsyncVirtualizingCollection<T>, ICollectionWithMoreItemsButton
        where T : class
    {
        private int _LastItemId = -1;

        public AsyncVirtualizingCollectionWithItemComparer(SynchronizationContext context, IItemsProvider<T> itemsProvider, int pageSize, int pageTimeout)
            : base(context, itemsProvider, pageSize, pageTimeout)
        {
        }

        /// <summary>
        /// Compares the last items.
        /// </summary>
        public void CompareLastItems()
        {
            if (IsProcessed)
                return;

            IsProcessed = true;

            ThreadPool.QueueUserWorkItem(o => CompareLastItemsWork(o));
        }

        /// <summary>
        /// Compares the last items in background.
        /// </summary>
        /// <param name="state">The state.</param>
        private void CompareLastItemsWork(object state)
        {
            int newLastItemId = ItemsProvider.FetchLastItemId();

            if (-1 == _LastItemId)
            {
                _LastItemId = newLastItemId;
                IsProcessed = false;

                return;
            }

            if (newLastItemId > _LastItemId)
                SynchronizationContext.Send(o => CompareLastItemsCompleted(o), new object[]
                {
                    newLastItemId
                });
            else
                IsProcessed = false;
        }

        /// <summary>
        /// Compares the last items completed.
        /// </summary>
        private void CompareLastItemsCompleted(object state)
        {
            var args = (object[])state;
            var newItemId = (int)args[0];

            _LastItemId = newItemId;

            if (FirstItemIsVisible && !HasMoreItems)
            {
                LoadMoreItems();
            }
            else
            {
                HasMoreItems = true;
                IsProcessed = false;
            }
        }

        public void LoadMoreItems()
        {
            IsProcessed = true;

            EmptyCache();

            ThreadPool.QueueUserWorkItem(o => LoadMoreItemsWork(o));
        }

        private void LoadMoreItemsWork(object state)
        {
            /*if (0 == Count && _LastItemId > 0)
            {
                RequestPage(0);
            }*/

            var items = this.Take(PageSize)
                .ToList();

            while (items.Any(e => e.IsLoading))
            {
                Thread.Sleep(10);
            }

            SynchronizationContext.Send(o => LoadMoreItemsCompleted(o), null);
        }

        private void LoadMoreItemsCompleted(object state)
        {
            HasMoreItems = false;

            FireCollectionReset();

            IsProcessed = false;
        }

        #region IsProcessed

        private bool _IsProcessed;

        public bool IsProcessed
        {
            [DebuggerStepThrough]
            get { return _IsProcessed; }
            set
            {
                if (value != _IsProcessed)
                {
                    _IsProcessed = value;
                    FirePropertyChanged("IsProcessed");
                }
            }
        }

        #endregion

        #region HasMoreItems

        private bool _HasMoreItems;

        public bool HasMoreItems
        {
            [DebuggerStepThrough]
            get { return _HasMoreItems; }
            set
            {
                if (value != _HasMoreItems)
                {
                    _HasMoreItems = value;
                    FirePropertyChanged("HasMoreItems");
                }
            }
        }

        #endregion

        #region Implementation of ICollectionWithMoreItemsButton

        #region FirstItemIsVisible

        private bool _FirstItemIsVisible;

        public bool FirstItemIsVisible
        {
            [DebuggerStepThrough]
            get { return _FirstItemIsVisible; }
            set
            {
                if (value != _FirstItemIsVisible)
                {
                    _FirstItemIsVisible = value;
                    FirePropertyChanged("FirstItemIsVisible");
                }
            }
        }

        #endregion

        #endregion
    }

    // ReSharper restore ConvertClosureToMethodGroup
}
