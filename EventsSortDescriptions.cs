using System;
using System.ComponentModel;
using System.Runtime.Serialization;

namespace zzz.Common.FilteringSorting
{
  [Serializable]
  public class EventsSortDescriptions : ISortDescriptions
  {
    
    public ListSortDirection? TimeStamp { get; set; }

    
    public ListSortDirection? Owners { get; set; }

    
    public ListSortDirection? Priorities { get; set; }

    
    public ListSortDirection? ObjectIds { get; set; }

    
    public string IndexName { get; set; }
  }
}
