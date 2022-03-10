using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace zzz.UI.Journals.Infrastructure.DataAccess.DataVirtualization
{
    /// <summary>
    /// Specialized list implementation that provides data virtualization. The collection is divided up into pages,
    /// and pages are dynamically fetched from the IItemsProvider when required. Stale pages are removed after a
    /// configurable period of time.
    /// Intended for use with large collections on a network or disk resource that cannot be instantiated locally
    /// due to memory consumption or fetch latency.
    /// </summary>
    /// <remarks>
    /// The IList implmentation is not fully complete, but should be sufficient for use as read only collection 
    /// data bound to a suitable ItemsControl.
    /// </remarks>
    /// <typeparam name="T"></typeparam>
    public class VirtualizingCollection<T> : IList<DataWrapper<T>>, IList where T : class
    {
        #region Constructors

        /// <summary>
        /// Initializes a new instance of the <see cref="VirtualizingCollection&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="itemsProvider">The items provider.</param>
        /// <param name="pageSize">Size of the page.</param>
        /// <param name="pageTimeout">The page timeout.</param>
        public VirtualizingCollection(IItemsProvider<T> itemsProvider, int pageSize, int pageTimeout)
        {
            _ItemsProvider = itemsProvider;
            _PageSize = pageSize;
            _PageTimeout = pageTimeout;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VirtualizingCollection&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="itemsProvider">The items provider.</param>
        /// <param name="pageSize">Size of the page.</param>
        public VirtualizingCollection(IItemsProvider<T> itemsProvider, int pageSize)
        {
            _ItemsProvider = itemsProvider;
            _PageSize = pageSize;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VirtualizingCollection&lt;T&gt;"/> class.
        /// </summary>
        /// <param name="itemsProvider">The items provider.</param>
        public VirtualizingCollection(IItemsProvider<T> itemsProvider)
        {
            _ItemsProvider = itemsProvider;
        }

        protected VirtualizingCollection(VirtualizingCollection<T> collection)
        {
            _ItemsProvider = collection.ItemsProvider;
            _PageSize = collection.PageSize;
            _PageTimeout = collection.PageTimeout;
        }

        #endregion

        #region ItemsProvider

        private readonly IItemsProvider<T> _ItemsProvider;

        /// <summary>
        /// Gets the items provider.
        /// </summary>
        /// <value>The items provider.</value>
        public IItemsProvider<T> ItemsProvider
        {
            [DebuggerStepThrough]
            get { return _ItemsProvider; }
        }

        #endregion

        #region PageSize

        private readonly int _PageSize = 100;

        /// <summary>
        /// Gets the size of the page.
        /// </summary>
        /// <value>The size of the page.</value>
        public int PageSize
        {
            [DebuggerStepThrough]
            get { return _PageSize; }
        }

        #endregion

        #region PageTimeout

        private readonly long _PageTimeout = 10000;

        /// <summary>
        /// Gets the page timeout.
        /// </summary>
        /// <value>The page timeout.</value>
        public long PageTimeout
        {
            get { return _PageTimeout; }
        }

        #endregion

        #region IList<DataWrapper<T>>, IList

        #region Count

        private int _Count = -1;

        /// <summary>
        /// Gets the number of elements contained in the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// The first time this property is accessed, it will fetch the count from the IItemsProvider.
        /// </summary>
        /// <value></value>
        /// <returns>
        /// The number of elements contained in the <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </returns>
        public int Count
        {
            get
            {
                if (_Count == -1)
                {
                    _Count = 0;

                    LoadCount();
                }
                return _Count;
            }
            protected set
            {
                _Count = value;
            }
        }

        #endregion

        #region Indexer

        /// <summary>
        /// Gets the item at the specified index. This property will fetch
        /// the corresponding page from the IItemsProvider if required.
        /// </summary>
        /// <value></value>
        public DataWrapper<T> this[int index]
        {
            get
            {
                // determine which page and offset within page
                int pageIndex = index / PageSize;
                int pageOffset = index % PageSize;

                // request primary page
                RequestPage(pageIndex);

                // if accessing upper 50% then request next page
                if (pageOffset > PageSize / 2 && pageIndex < (Count / PageSize) - 1)
                    RequestPage(pageIndex + 1);

                // if accessing lower 50% then request prev page
                if (pageOffset < PageSize / 2 && pageIndex > 0)
                    RequestPage(pageIndex - 1);

                // remove stale pages
                CleanUpPages();

                // return requested item
                return _Pages[pageIndex].Items[pageOffset];
            }
            set { throw new NotSupportedException(); }
        }

        object IList.this[int index]
        {
            get { return this[index]; }
            set { throw new NotSupportedException(); }
        }

        #endregion

        #region IEnumerator<DataWrapper<T>>, IEnumerator

        /// <summary>
        /// Returns an enumerator that iterates through the collection.
        /// </summary>
        /// <remarks>
        /// This method should be avoided on large collections due to poor performance.
        /// </remarks>
        /// <returns>
        /// A <see cref="T:System.Collections.Generic.IEnumerator`1"/> that can be used to iterate through the collection.
        /// </returns>
        public IEnumerator<DataWrapper<T>> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
            {
                yield return this[i];
            }
        }

        /// <summary>
        /// Returns an enumerator that iterates through a collection.
        /// </summary>
        /// <returns>
        /// An <see cref="T:System.Collections.IEnumerator"/> object that can be used to iterate through the collection.
        /// </returns>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        #endregion

        #region Add

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <param name="item">The object to add to the <see cref="T:System.Collections.Generic.ICollection`1"/>.</param>
        /// <exception cref="T:System.NotSupportedException">
        /// The <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only.
        /// </exception>
        public void Add(DataWrapper<T> item)
        {
            throw new NotSupportedException();
        }

        int IList.Add(object value)
        {
            throw new NotSupportedException();
        }

        #endregion

        #region Contains

        bool IList.Contains(object value)
        {
            return Contains((DataWrapper<T>)value);
        }

        /// <summary>
        ///  Determines whether the collection contains a specific value.
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="T:System.Collections.Generic.ICollection`1"/>.</param>
        public bool Contains(DataWrapper<T> item)
        {
            return _Pages.Values.Any(page => page.Items.Contains(item));
        }

        #endregion

        #region Clear

        /// <summary>
        /// TODO
        /// </summary>
        public void Clear()
        {
            throw new NotSupportedException();
        }

        #endregion

        #region IndexOf

        int IList.IndexOf(object value)
        {
            return IndexOf((DataWrapper<T>)value);
        }

        /// <summary>
        /// TODO
        /// </summary>
        /// <param name="item">The object to locate in the <see cref="T:System.Collections.Generic.IList`1"/>.</param>
        /// <returns>
        /// TODO
        /// </returns>
        public int IndexOf(DataWrapper<T> item)
        {
            foreach (KeyValuePair<int, DataPage<T>> keyValuePair in _Pages)
            {
                int indexWithinPage = keyValuePair.Value.Items.IndexOf(item);

                if (indexWithinPage != -1)
                {
                    return PageSize * keyValuePair.Key + indexWithinPage;
                }
            }
            return -1;
        }

        #endregion

        #region Insert

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <param name="index">The zero-based index at which <paramref name="item"/> should be inserted.</param>
        /// <param name="item">The object to insert into the <see cref="T:System.Collections.Generic.IList`1"/>.</param>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        /// 	<paramref name="index"/> is not a valid index in the <see cref="T:System.Collections.Generic.IList`1"/>.
        /// </exception>
        /// <exception cref="T:System.NotSupportedException">
        /// The <see cref="T:System.Collections.Generic.IList`1"/> is read-only.
        /// </exception>
        public void Insert(int index, DataWrapper<T> item)
        {
            throw new NotSupportedException();
        }

        void IList.Insert(int index, object value)
        {
            Insert(index, (DataWrapper<T>)value);
        }

        #endregion

        #region Remove

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <param name="index">The zero-based index of the item to remove.</param>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        /// 	<paramref name="index"/> is not a valid index in the <see cref="T:System.Collections.Generic.IList`1"/>.
        /// </exception>
        /// <exception cref="T:System.NotSupportedException">
        /// The <see cref="T:System.Collections.Generic.IList`1"/> is read-only.
        /// </exception>
        public void RemoveAt(int index)
        {
            throw new NotSupportedException();
        }

        void IList.Remove(object value)
        {
            throw new NotSupportedException();
        }

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <param name="item">The object to remove from the <see cref="T:System.Collections.Generic.ICollection`1"/>.</param>
        /// <returns>
        /// true if <paramref name="item"/> was successfully removed from the <see cref="T:System.Collections.Generic.ICollection`1"/>; otherwise, false. This method also returns false if <paramref name="item"/> is not found in the original <see cref="T:System.Collections.Generic.ICollection`1"/>.
        /// </returns>
        /// <exception cref="T:System.NotSupportedException">
        /// The <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only.
        /// </exception>
        public bool Remove(DataWrapper<T> item)
        {
            throw new NotSupportedException();
        }

        #endregion

        #region CopyTo

        /// <summary>
        /// Not supported.
        /// </summary>
        /// <param name="array">The one-dimensional <see cref="T:System.Array"/> that is the destination of the elements copied from <see cref="T:System.Collections.Generic.ICollection`1"/>. The <see cref="T:System.Array"/> must have zero-based indexing.</param>
        /// <param name="arrayIndex">The zero-based index in <paramref name="array"/> at which copying begins.</param>
        /// <exception cref="T:System.ArgumentNullException">
        /// 	<paramref name="array"/> is null.
        /// </exception>
        /// <exception cref="T:System.ArgumentOutOfRangeException">
        /// 	<paramref name="arrayIndex"/> is less than 0.
        /// </exception>
        /// <exception cref="T:System.ArgumentException">
        /// 	<paramref name="array"/> is multidimensional.
        /// -or-
        /// <paramref name="arrayIndex"/> is equal to or greater than the length of <paramref name="array"/>.
        /// -or-
        /// The number of elements in the source <see cref="T:System.Collections.Generic.ICollection`1"/> is greater than the available space from <paramref name="arrayIndex"/> to the end of the destination <paramref name="array"/>.
        /// -or-
        /// Type <paramref name="T"/> cannot be cast automatically to the type of the destination <paramref name="array"/>.
        /// </exception>
        public void CopyTo(DataWrapper<T>[] array, int arrayIndex)
        {
            throw new NotSupportedException();
        }

        void ICollection.CopyTo(Array array, int index)
        {
            throw new NotSupportedException();
        }

        #endregion

        #region Misc

        /// <summary>
        /// Gets an object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection"/>.
        /// </summary>
        /// <value></value>
        /// <returns>
        /// An object that can be used to synchronize access to the <see cref="T:System.Collections.ICollection"/>.
        /// </returns>
        public object SyncRoot
        {
            get { return this; }
        }

        /// <summary>
        /// Gets a value indicating whether access to the <see cref="T:System.Collections.ICollection"/> is synchronized (thread safe).
        /// </summary>
        /// <value></value>
        /// <returns>Always false.
        /// </returns>
        public bool IsSynchronized
        {
            [DebuggerStepThrough]
            get { return false; }
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="T:System.Collections.Generic.ICollection`1"/> is read-only.
        /// </summary>
        /// <value></value>
        /// <returns>Always true.
        /// </returns>
        public bool IsReadOnly
        {
            [DebuggerStepThrough]
            get { return true; }
        }

        /// <summary>
        /// Gets a value indicating whether the <see cref="T:System.Collections.IList"/> has a fixed size.
        /// </summary>
        /// <value></value>
        /// <returns>Always false.
        /// </returns>
        public bool IsFixedSize
        {
            [DebuggerStepThrough]
            get { return false; }
        }

        #endregion

        #endregion

        #region Paging

        private Dictionary<int, DataPage<T>> _Pages = new Dictionary<int, DataPage<T>>();

        /// <summary>
        /// Cleans up any stale pages that have not been accessed in the period dictated by PageTimeout.
        /// </summary>
        public void CleanUpPages()
        {
            int[] keys = _Pages.Keys.ToArray();

            foreach (int key in keys)
            {
                // page 0 is a special case, since WPF ItemsControl access the first item frequently
                if (key != 0 && (DateTime.Now - _Pages[key].TouchTime).TotalMilliseconds > PageTimeout)
                {
                    var removePage = true;

                    DataPage<T> page;

                    if (_Pages.TryGetValue(key, out page))
                    {
                        removePage = !page.IsInUse;
                    }

                    if (removePage)
                    {
                        _Pages.Remove(key);
                        Trace.WriteLine("Removed Page: " + key);
                    }
                }
            }
        }

        private readonly object _RequestPageLock = new object();

        /// <summary>
        /// Makes a request for the specified page, creating the necessary slots in the dictionary,
        /// and updating the page touch time.
        /// </summary>
        /// <param name="pageIndex">Index of the page.</param>
        protected virtual void RequestPage(int pageIndex)
        {
            int pageLength;

            lock (_RequestPageLock)
            {
                if (!_Pages.ContainsKey(pageIndex))
                {
                    // Create a page of empty data wrappers.
                    pageLength = Math.Min(PageSize, Count - pageIndex * PageSize);

                    var page = new DataPage<T>(pageIndex * PageSize, pageLength);

                    _Pages[pageIndex] = page;


                    LoadPage(pageIndex, pageLength);

                    Trace.WriteLine("Added page: " + pageIndex);
                }
                else
                {
                    _Pages[pageIndex].TouchTime = DateTime.Now;
                }
            }
        }

        /// <summary>
        /// Populates the page within the dictionary.
        /// </summary>
        /// <param name="pageIndex">Index of the page.</param>
        /// <param name="dataItems"></param>
        protected virtual DataPage<T> PopulatePage(int pageIndex, IList<T> dataItems)
        {
            Trace.WriteLine("Page populated: " + pageIndex);

            DataPage<T> page;

            if (_Pages.TryGetValue(pageIndex, out page))
            {
                page.Populate(dataItems);
            }

            return page;
        }

        /// <summary>
        /// Removes all cached pages. This is useful when the count of the 
        /// underlying collection changes.
        /// </summary>
        protected void EmptyCache()
        {
            _Pages = new Dictionary<int, DataPage<T>>();
        }

        #endregion

        #region Load methods

        /// <summary>
        /// Loads the count of items.
        /// </summary>
        protected virtual void LoadCount()
        {
            Count = FetchCount();
        }

        /// <summary>
        /// Loads the page of items.
        /// </summary>
        /// <param name="pageIndex">Index of the page.</param>
        /// <param name="pageLength">Number of items in the page.</param>
        protected virtual void LoadPage(int pageIndex, int pageLength)
        {
            int count;

            PopulatePage(pageIndex, FetchPage(pageIndex, pageLength, out count));

            Count = count;
        }

        #endregion

        #region Fetch methods

        /// <summary>
        /// Fetches the requested page from the IItemsProvider.
        /// </summary>
        /// <param name="pageIndex">Index of the page.</param>
        /// <param name="pageLength"></param>
        /// <param name="count"></param>
        /// <returns></returns>
        protected IList<T> FetchPage(int pageIndex, int pageLength, out int count)
        {
            return ItemsProvider.FetchRange(pageIndex * PageSize, pageLength, out count);
        }

        /// <summary>
        /// Fetches the count of itmes from the IItemsProvider.
        /// </summary>
        /// <returns></returns>
        protected int FetchCount()
        {
            return ItemsProvider.FetchCount();
        }

        #endregion
    }
}
