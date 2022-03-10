
namespace zzz.UI.Journals.Infrastructure.DataAccess.Providers
{
    public delegate List<JournalEntry> GetEntriesDelegate(EventsLimitsDescriptions eventsLimitsDescriptions, EventsFilterDescriptions eventsFilterDescriptions,
        EventsSortDescriptions eventsSortDescriptions);

    public delegate int GetMaxIDFromEntriesDelegate(EventsFilterDescriptions eventsFilterDescriptions);

    public class JournalEntriesProvider : IItemsProvider<JournalEntryViewModel>
    {
        #region Fields

        private readonly ILogger _Logger;
        private readonly IEUSettings _EUSettings;
        private readonly IEUNumericFormat _NumericFormat;
        private readonly Configuration.Configuration _Configuration;
        private readonly GetEntriesDelegate _GetEntries;
        private readonly GetMaxIDFromEntriesDelegate _GetMaxIDFromEntries;
        private readonly MakeEntryColorBySettingsDelegate _makeEntryColorBySettings;

        private readonly EventsFilterDescriptions _FilterDescriptions;
        private readonly EventsSortDescriptions _SortDescriptions;

        private readonly object _FetchCountLock = new object();
        private readonly object _FetchRangeLock = new object();
        private readonly object _FetchLastItemIdLock = new object();

        private readonly int _PageSize;
        private readonly ICommonApplicationSettings _Settings;
        private readonly Dictionary<int, string> _ColorKeyByMType;
        private int _Count;

        #endregion

        public JournalEntriesProvider(ILogger logger,
                                      EventsFilterDescriptions fd,
                                      EventsSortDescriptions sd,
                                      int pageSize,
                                      ICommonApplicationSettings settings,
                                      Dictionary<int, string> colorKeyByMType,
                                      IEUSettings euSettings,
                                      IEUNumericFormat numericFormat,
                                      Configuration.Configuration configuration,
                                      GetEntriesDelegate getEntries,
                                      GetMaxIDFromEntriesDelegate getMaxIDFromEntries,
                                      MakeEntryColorBySettingsDelegate makeEntryColorBySettings)
        {
            _Logger = logger;
            _EUSettings = euSettings;
            _NumericFormat = numericFormat;
            _Configuration = configuration;
            _GetEntries = getEntries;
            _GetMaxIDFromEntries = getMaxIDFromEntries;
            _makeEntryColorBySettings = makeEntryColorBySettings;

            _PageSize = pageSize;
            _Settings = settings;
            _ColorKeyByMType = colorKeyByMType;

            _FilterDescriptions = fd;
            _SortDescriptions = sd;
        }

        #region Implementation of IItemsProvider<JournalEntryViewModel>

        public int FetchCount()
        {
            lock (_FetchCountLock)
            {
                _Logger.Debug("---------------------");
                _Logger.Debug("FetchCount");

                EventsLimitsDescriptions limitsDescriptions = RHDAExtensions.GetEventsLimitsDescriptions(_PageSize, 0);
                List<JournalEntry> entries = _GetEntries(limitsDescriptions, _FilterDescriptions, _SortDescriptions);

                var overallCount = entries.Count;

                _Logger.Debug($"FetchCount entries.Count: {entries.Count}");

                if (overallCount < _PageSize)
                {
                    _Count = overallCount;

                    _Logger.Debug($"FetchCount _Count: {_Count}");

                    return _Count;
                }

                EventsLimitsDescriptions limitsDescriptions2 = RHDAExtensions.GetEventsLimitsDescriptions(_PageSize, _PageSize);
                List<JournalEntry> entries2 = _GetEntries(limitsDescriptions2, _FilterDescriptions, _SortDescriptions);

                _Logger.Debug($"FetchCount entries2.Count: {entries2.Count}");

                _Count = overallCount + entries2.Count;

                _Logger.Debug($"FetchCount _Count: {_Count}");
            }

            return _Count;
        }

        public IList<JournalEntryViewModel> FetchRange(int startIndex, int pageCount, out int overallCount)
        {
            // TODO: remove lock
            lock (_FetchRangeLock)
            {
                _Logger.Debug("---------------------");
                _Logger.Debug($"FetchRange startIndex: {startIndex}, pageCount: {pageCount}");

                var result = new List<JournalEntryViewModel>();

                EventsLimitsDescriptions ld = RHDAExtensions.GetEventsLimitsDescriptions(pageCount, startIndex);
                List<JournalEntry> entries = _GetEntries(ld, _FilterDescriptions, _SortDescriptions);

                _Logger.Debug(string.Format("FetchRange entries.Count: {0}", entries.Count));

                if (entries.Count > 0)
                {
                    JournalExtensions.FormatEntries(entries, _EUSettings, _NumericFormat, _Configuration);

                    result.AddRange(entries.Select((entry, index) =>
                    {
                        var journalEntry = JournalEntryModel.Create(entry, _EUSettings, _NumericFormat, _Configuration);
                        return new JournalEntryViewModel(_Configuration, journalEntry);
                    }));

                    if (null != _Settings && null != _ColorKeyByMType)
                        JournalExtensions.ColorizeEntries(result, _Settings, _ColorKeyByMType, _makeEntryColorBySettings);

                    // TODO: remove check?
                    if (0 == startIndex)
                        _Count = entries.Count;

                    if (entries.Count == _PageSize && startIndex + _PageSize == _Count)
                    {
                        EventsLimitsDescriptions limitsDescriptions2 = RHDAExtensions.GetEventsLimitsDescriptions(pageCount, startIndex + _PageSize);
                        List<JournalEntry> entries2 = _GetEntries(limitsDescriptions2, _FilterDescriptions, _SortDescriptions);

                        _Logger.Debug($"FetchRange entries2.Count: {entries2.Count}");

                        _Count = startIndex + _PageSize + entries2.Count;
                    }
                }

                overallCount = _Count;

                _Logger.Debug($"FetchRange _Count: {_Count}");

                return result;
            }
        }

        public int FetchLastItemId()
        {
            int maxId;

            lock (_FetchLastItemIdLock)
            {
                _Logger.Debug("---------------------");
                _Logger.Debug("FetchLastItemId");

                maxId = _GetMaxIDFromEntries(_FilterDescriptions);

                _Logger.Debug($"FetchLastItemId maxId: {maxId}");
            }

            return maxId;
        }

        #endregion
    }
}
