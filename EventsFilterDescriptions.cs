namespace zzz.Common.FilteringSorting
{
  [Serializable]
  public class EventsFilterDescriptions : IFilterDescriptions
  {
    
    public int? MinId { get; set; }

    
    public int? MaxId { get; set; }

    
    public DateTime? DateTimeStart { get; set; }

    
    public DateTime? DateTimeEnd { get; set; }

    
    public List<EventSourceMaskType> SourceMask { get; set; }

    
    public List<EventOwner> Owners { get; set; }

    
    public List<int> Priorities { get; set; }

    
    public List<int> ObjectIds { get; set; }

    
    public string Message { get; set; }

    
    public StringFilterCondition MessageFilterCondition { get; set; }

    
    public List<Guid> GUIDs { get; set; }

    
    public List<int> Sources { get; set; }
  }
}
