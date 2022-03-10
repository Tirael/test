using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace zzz.UI.Journals.Infrastructure.DataAccess.DataVirtualization
{
    public class DataWrapper<T> : INotifyPropertyChanged where T : class
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public DataWrapper(int index)
        {
            _Index = index;
        }

        #region Index

        private int _Index;

        public int Index
        {
            get { return _Index; }
        }

        #endregion

        #region ItemNumber

        public int ItemNumber
        {
            get { return _Index + 1; }
        }

        #endregion

        #region IsLoading

        public bool IsLoading
        {
            get { return Data == null; }
        }

        #endregion

        #region Data

        private T _Data;

        public T Data
        {
            get { return _Data; }
            internal set
            {
                _Data = value;
                OnPropertyChanged("Data");
                OnPropertyChanged("IsLoading");
            }
        }

        #endregion

        #region IsInUse

        public bool IsInUse
        {
            get { return PropertyChanged != null; }
        }

        #endregion

        private void OnPropertyChanged(string propertyName)
        {
            System.Diagnostics.Debug.Assert(GetType().GetProperty(propertyName) != null);

            var handler = PropertyChanged;

            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
