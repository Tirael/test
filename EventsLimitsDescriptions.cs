using System;
using System.Runtime.Serialization;

namespace zzz.Common.FilteringSorting
{
  [Serializable]
  public class EventsLimitsDescriptions
  {
    
    public int? StartIndex { get; set; }

    
    public int RowCount { get; set; }
  }
}
