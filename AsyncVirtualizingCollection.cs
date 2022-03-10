using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Data;

namespace zzz.UI.Journals.Infrastructure.DataAccess.DataVirtualization
{
    // ReSharper disable ConvertClosureToMethodGroup
    /// <summary>
    /// Derived VirtualizatingCollection, performing loading asychronously.
    /// </summary>
    /// <typeparam name="T">The type of items in the collection</typeparam>
    public class AsyncVirtualizingCollection<T> : VirtualizingCollection<T>, INotifyCollectionChanged, INotifyPropertyChanged
        where T : class
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AsyncVirtualizingCollection&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="context">Synchronization context</param>
        /// <param name="itemsProvider">The items provider.</param>
        /// <param name="pageSize">Size of the page.</param>
        /// <param name="pageTimeout">The page timeout.</param>
        public AsyncVirtualizingCollection(SynchronizationContext context, IItemsProvider<T> itemsProvider, int pageSize, int pageTimeout)
            : base(itemsProvider, pageSize, pageTimeout)
        {
            _SynchronizationContext = context;
        }

        #region SynchronizationContext

        private readonly SynchronizationContext _SynchronizationContext;

        /// <summary>
        /// Gets the synchronization context used for UI-related operations. This is obtained as
        /// the current SynchronizationContext when the AsyncVirtualizingCollection is created.
        /// </summary>
        /// <value>The synchronization context.</value>
        protected SynchronizationContext SynchronizationContext
        {
            get { return _SynchronizationContext; }
        }

        #endregion

        #region INotifyCollectionChanged

        /// <summary>
        /// Occurs when the collection changes.
        /// </summary>
        public event NotifyCollectionChangedEventHandler CollectionChanged;

        /// <summary>
        /// Raises the <see cref="E:CollectionChanged"/> event.
        /// </summary>
        /// <param name="e">The <see cref="System.Collections.Specialized.NotifyCollectionChangedEventArgs"/> instance containing the event data.</param>
        protected virtual void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
        {
            NotifyCollectionChangedEventHandler h = CollectionChanged;

            if (h != null)
                h(this, e);
        }

        /// <summary>
        /// Fires the collection reset event.
        /// </summary>
        protected void FireCollectionReset()
        {
            var e = new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset);

            OnCollectionChanged(e);
        }

        #endregion

        #region INotifyPropertyChanged

        /// <summary>
        /// Occurs when a property value changes.
        /// </summary>
        public event PropertyChangedEventHandler PropertyChanged;

        /// <summary>
        /// Raises the <see cref="E:PropertyChanged"/> event.
        /// </summary>
        /// <param name="e">The <see cref="System.ComponentModel.PropertyChangedEventArgs"/> instance containing the event data.</param>
        protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
        {
            PropertyChangedEventHandler h = PropertyChanged;

            if (h != null)
                h(this, e);
        }

        /// <summary>
        /// Fires the property changed event.
        /// </summary>
        /// <param name="propertyName">Name of the property.</param>
        protected void FirePropertyChanged(string propertyName)
        {
            var e = new PropertyChangedEventArgs(propertyName);
            OnPropertyChanged(e);
        }

        #endregion

        #region IsLoading

        private bool _IsLoading;

        /// <summary>
        /// Gets or sets a value indicating whether the collection is loading.
        /// </summary>
        /// <value>
        /// 	<c>true</c> if this collection is loading; otherwise, <c>false</c>.
        /// </value>
        public bool IsLoading
        {
            [DebuggerStepThrough]
            get { return _IsLoading; }
            set
            {
                if (value != _IsLoading)
                {
                    _IsLoading = value;
                    FirePropertyChanged("IsLoading");
                }
            }
        }

        #endregion

        #region IsInitializing

        private bool _IsInitializing;

        public bool IsInitializing
        {
            [DebuggerStepThrough]
            get { return _IsInitializing; }
            set
            {
                if (value != _IsInitializing)
                {
                    _IsInitializing = value;
                    FirePropertyChanged("IsInitializing");
                }
            }
        }

        #endregion

        #region Load overrides

        /// <summary>
        /// Asynchronously loads the count of items.
        /// </summary>
        protected override void LoadCount()
        {
            if (Count == 0)
            {
                IsInitializing = true;
            }

            ThreadPool.QueueUserWorkItem(o => LoadCountWork(o));
        }

        /// <summary>
        /// Performed on background thread.
        /// </summary>
        /// <param name="args">None required.</param>
        private void LoadCountWork(object args)
        {
            int count = FetchCount();

            SynchronizationContext.Send(o => LoadCountCompleted(o), count);
        }

        /// <summary>
        /// Performed on UI-thread after LoadCountWork.
        /// </summary>
        /// <param name="args">Number of items returned.</param>
        protected virtual void LoadCountCompleted(object args)
        {
            var newCount = (int)args;

            TakeNewCount(newCount);

            IsInitializing = false;
        }

        private void TakeNewCount(int newCount)
        {
            if (newCount == Count)
                return;

            Count = newCount;

            EmptyCache();

            FireCollectionReset();
        }

        /// <summary>
        /// Asynchronously loads the page.
        /// </summary>
        /// <param name="pageIndex"></param>
        /// <param name="pageLength"></param>
        protected override void LoadPage(int pageIndex, int pageLength)
        {
            IsLoading = true;

            ThreadPool.QueueUserWorkItem(o => LoadPageWork(o), new[]
            {
                pageIndex, pageLength
            });
        }

        /// <summary>
        /// Performed on background thread.
        /// </summary>
        /// <param name="state">int[] { pageIndex, pageLength }</param>
        private void LoadPageWork(object state)
        {
            var args = (int[])state;

            int pageIndex = args[0];
            int pageLength = args[1];

            int overallCount;

            IList<T> dataItems = FetchPage(pageIndex, pageLength, out overallCount);

            SynchronizationContext.Send(o => LoadPageCompleted(o), new object[]
            {
                pageIndex, dataItems, overallCount
            });
        }

        /// <summary>
        /// Performed on UI-thread after LoadPageWork.
        /// </summary>
        /// <param name="state">object[] { int pageIndex, IList(T) page, int overallCount }</param>
        private void LoadPageCompleted(object state)
        {
            var args = (object[])state;

            var pageIndex = (int)args[0];
            var dataItems = (IList<T>)args[1];
            var newCount = (int)args[2];

            var newPage = PopulatePage(pageIndex, dataItems);

            if (null != newPage)
                TakeNewCount(newCount, newPage.Items);

            IsLoading = false;
        }

        private void TakeNewCount(int newCount, IEnumerable<DataWrapper<T>> items)
        {
            if (newCount == Count)
                return;

            Count = newCount;
            FireCollectionItemsIncremented(items);
        }

        private void FireCollectionItemsIncremented(IEnumerable<DataWrapper<T>> newItems)
        {
            OnCollectionChangedMultiItem(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Add, newItems));
        }

        protected virtual void OnCollectionChangedMultiItem(NotifyCollectionChangedEventArgs e)
        {
            NotifyCollectionChangedEventHandler handlers = CollectionChanged;

            if (handlers == null)
                return;

            foreach (NotifyCollectionChangedEventHandler handler in handlers.GetInvocationList())
            {
                if (handler.Target is CollectionView)
                    ((CollectionView)handler.Target).Refresh();
                else
                    handler(this, e);
            }
        }

        #endregion
    }

    // ReSharper restore ConvertClosureToMethodGroup
}
