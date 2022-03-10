
namespace zzz.JournalModule.ViewModels
{
    // ReSharper disable ClassNeverInstantiated.Global
    /// <summary>
    /// Вкладка "Общий журнал".
    /// </summary>
    public sealed class CommonJournalViewModel : SynchronizedDSSChangesAwareViewModelBase, /*IRHDAAvailableAware,*/ IDataErrorInfo
    // ReSharper restore ClassNeverInstantiated.Global
    {
        private readonly IEUNumericFormat _NumericFormat;
        private readonly JournalModuleSettingsViewModel _ModuleSettings;
        private readonly JournalFactory _Factory;
        private readonly IFileDialogService _FileDialogService;
        private readonly IBackupSettings _BackupSettings;
        private readonly ILDSServicesConnectors _ServicesConnectors;
        private readonly ISchedulerProvider _SchedulerProvider;
        private readonly IExportManager _ExportManager;
        private readonly string _keyword;

        private const double DefaultStartDateTimeInHours = 24;
        private IDisposable _AllEntriesIsInitializing;

        #region Constructors

        // ReSharper disable UnusedMember.Global

        public CommonJournalViewModel(IDSSHelper dssHelper,
                                      IEventAggregator eventAggregator,
                                      ILoggerFacade logger,
                                      IUnityContainer container,
                                      IScheduler scheduler,
                                      IExportManager exportManager,
                                      IEUSettings euSettings,
                                      IEUNumericFormat numericFormat,
                                      ApplicationSettingsModel settings,
                                      JournalModuleSettingsViewModel moduleSettings,
                                      JournalFactory factory,
                                      IFileDialogService fileDialogService,
                                      IHelpContent helpContent,
                                      IBackupSettings backupSettings,
                                      ILDSServicesConnectors servicesConnectors,
                                      ISchedulerProvider schedulerProvider)
            : base(container, eventAggregator, dssHelper, logger, euSettings, settings, scheduler)
        {
            _NumericFormat = numericFormat;
            _ModuleSettings = moduleSettings;
            _Factory = factory;
            _FileDialogService = fileDialogService;
            _BackupSettings = backupSettings;
            _ServicesConnectors = servicesConnectors;
            _SchedulerProvider = schedulerProvider;
            _ExportManager = exportManager;
            _Model = CommonJournalModel.Create();
            _keyword = ((IJournalsHelpContent)helpContent).CommonJournal;

            Disposable.Add(_ExportEntriesWorker);
            Disposable.Add(_loadEntriesFromArchiveWorker);
            Disposable.Add(_FilterObjectsLoadingWorker);

            Initialize();
        }

        // ReSharper restore UnusedMember.Global

        #endregion

        private readonly CommonJournalModel _Model;

        protected override void ApplyFullConfiguration(Configuration configuration)
        {
            CreateCollections();

            _SchedulerProvider.MainThread.Schedule(
                () =>
                {
                    try
                    {
                        if (!_FilterObjectsLoadingWorker.IsBusy)
                            _FilterObjectsLoadingWorker.RunWorkerAsync();

                        FilterByColorsList.Clear();
                        SourceListEditConvenienceEx.AddRange(FilterByColorsList, _Factory.CreateJournalColorGroups(_ModuleSettings.ColorGroupsBindable));

                        ApplyPartialConfiguration(configuration);

                        LastConfigurationFullUpdateTimestamp = DSSHelper.LastConfigurationFullUpdateTimestamp;
                    }
                    catch (Exception ex)
                    {
                        Logger.LogException(ex);
                    }
                });
        }

        protected override void ApplyDSSLayerChanged(SeventhLayer currentLayer)
        {
            if (NewConfigurationProcessingIsActive || NewLayerProcessingIsActive || null == currentLayer ||
                DataSourceMode.RealTime != JournalMode)
                return;

            SetAndWaitingNewLayerProcessing();

            if (null == Configuration)
            {
                if (!ApplyConfigurationFromDSSHelper())
                {
                    ResetNewLayerProcessing();

                    return;
                }
            }

            if (BusyIndicatorState)
                _SchedulerProvider.MainThread.Schedule(() => BusyIndicatorState = false);

            try
            {
                if (null != AllEntries)
                {
                    lock (_allEntriesLocker)
                    {
                        AllEntries.CompareLastItems();
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }

            ResetNewLayerProcessing();
        }

        private JournalEntriesProvider _JournalEntriesProvider;
        private const int PageSize = 500;
        private const int TimePageInMemory = 20000;

        private void CreateAllEntries(Action action)
        {
            _SchedulerProvider.MainThread.Schedule(() =>
            {
                _Model.AppliedFilterDescriptions = GetEventsFilterDescriptions(_Model, JournalMode, FilterByMessageText);

                EventsFilterDescriptions filterDescriptions = _Model.AppliedFilterDescriptions;

                var sortDescriptions = new SortDescriptionCollection
                {
                    ModuleConstants.DefaultTimeStampSortDescription
                };

                EventsSortDescriptions eventsSortDescriptions = RHDAExtensions.GetEventsSortDescriptions(sortDescriptions);

                #region colors

                var colorKeyByMType = new Dictionary<int, string>();

                foreach (JournalColorGroupViewModel colorGroup in _ModuleSettings.ColorGroupsBindable)
                {
                    foreach (JournalColorGroupMessageViewModel message in colorGroup.Messages.Items)
                    {
                        colorKeyByMType.Add(message.MType, colorGroup.ColorKey);
                    }
                }

                #endregion

                var logger = LogManager.GetCurrentClassLogger();

                GetEntriesDelegate getEntries = (ld, fd, sd) => null == fd.Sources
                                                                ? new List<JournalEntry>()
                                                                : DSSHelper.GetEntries(ld, fd, sd);
                GetMaxIDFromEntriesDelegate getMaxIDFromEntries = fd => null == fd.Sources
                                                                        ? 0
                                                                        : DSSHelper.GetMaxIDFromEntries(fd);

                _JournalEntriesProvider = new JournalEntriesProvider(logger,
                                                                     filterDescriptions,
                                                                     eventsSortDescriptions,
                                                                     PageSize,
                                                                     _Settings,
                                                                     colorKeyByMType,
                                                                     EUSettings,
                                                                     _NumericFormat,
                                                                     DSSHelper.Configuration,
                                                                     getEntries,
                                                                     getMaxIDFromEntries,
                                                                     ColorGroupExtension.ColorForColorKey);

                AllEntries = new AsyncVirtualizingCollectionWithItemComparer<JournalEntryViewModel>(SynchronizationContext.Current, _JournalEntriesProvider, PageSize,
                                                                                                    TimePageInMemory);
            });

            _SchedulerProvider.MainThread.Schedule(() =>
            {
            });

            _SchedulerProvider.MainThread.Schedule(() =>
            {
                if (ApplyFilterIsProcessing)
                {
                    ICollectionView view = CollectionViewSource.GetDefaultView(AllEntries);

                    if (null != view)
                        view.Refresh();

                    FiltersIsChanged = false;
                }

                if (null != action)
                    action.Invoke();
            });
        }

        protected override void CreateCollections()
        {
            CreateAllEntries(() =>
            {
                try
                {
                    #region FilterByObjectsList

                    FilterByObjectsList = new RadObservableCollectionEx<CategoryViewModel>();
                    FilterByObjectsList.CollectionChanged += FilterObjectsListOnCollectionChanged;

                    FilterObjectsListView = (ListCollectionView)CollectionViewSource.GetDefaultView(FilterByObjectsList);
                    FilterObjectsListView.Filter = FilterObjectsListViewContains;
                    FilterObjectsListView.Refresh();

                    #endregion
                }
                catch (Exception ex)
                {
                    Logger.LogException(ex);
                }
            });
        }

        private bool _ResetExportIsCheckedFromDialog;

        private void SubscribeToPropertiesChanged()
        {
            #region IsActive

            this.ObservableForProperty(vm => vm.IsActive)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(isActive =>
                {
                    if (isActive)
                    {
                        _SchedulerProvider.MainThread.Schedule(() =>
                        {
                            var changeActiveViewEvent = EventAggregator.GetEvent<ChangeActiveViewEvent>();
                            changeActiveViewEvent.Publish(Strings.CommonJournal_Name);

                            BusyIndicatorState = true;
                            BusyIndicatorMessage = "Обновление данных...";
                            BusyIndicatorIsIndeterminate = true;
                        });

                        // TODO обновить colorgroups из настроек - новые добавить, удаленные - удалить и применить фильтр
                        var moduleSettingsIsChanged = false;

                        if (_ModuleSettings.ColorGroupsBindable.Count != FilterByColorsList.Count)
                        {
                            moduleSettingsIsChanged = true;
                        }
                        else
                        {
                            foreach (JournalColorGroupViewModel colorGroup in _ModuleSettings.ColorGroupsBindable)
                            {
                                JournalColorGroupViewModel existingColorGroup = FilterByColorsList.Items.FirstOrDefault(e => e.ColorKey == colorGroup.ColorKey);

                                if (null != existingColorGroup)
                                {
                                    if (colorGroup.Messages.Count != existingColorGroup.Messages.Count)
                                    {
                                        moduleSettingsIsChanged = true;

                                        break;
                                    }

                                    bool distinct = colorGroup.Messages.Items.Except(existingColorGroup.Messages.Items).Any();

                                    if (distinct)
                                        continue;

                                    moduleSettingsIsChanged = true;

                                    break;
                                }

                                moduleSettingsIsChanged = true;

                                break;
                            }
                        }

                        if (moduleSettingsIsChanged)
                        {
                            RefreshFilterByColorsList(_ModuleSettings);
                        }

                        CreateAllEntries(() =>
                        {
                            _AllEntriesIsInitializing = this.ObservableForProperty(vm => vm.AllEntries.IsInitializing)
                            .ObserveOn(Scheduler)
                            .Where(x => null != x)
                            .Select(x => null != x.Sender._AllEntries && x.GetValue())
                            .Subscribe(isInitializing => _SchedulerProvider.MainThread.Schedule(() => BusyIndicatorState = isInitializing));

                            Disposable.Add(_AllEntriesIsInitializing);

                            DSSConfigurationFullChanged(DSSHelper.Configuration);
                        });
                    }
                    else
                    {
                        SetAndWaitingNewLayerProcessing();

                        if (DataSourceMode.RealTime == JournalMode)
                            UnsubscribeFromDSSEvents(new[]
                            {
                                DSSEventType.DSSLayerChanged, DSSEventType.DSSConfigurationFullChanged
                            });

                        if (null != _AllEntriesIsInitializing)
                        {
                            _AllEntriesIsInitializing.Dispose();
                            _AllEntriesIsInitializing = null;
                        }

                        lock (_allEntriesLocker)
                        {
                            AllEntries = null;
                        }

                        ColorsListIsDropDownOpen = false;

                        ResetNewLayerProcessing();
                    }
                });

            this.ObservableForProperty(vm => vm.IsActive)
                .Throttle(TimeSpan.FromMilliseconds((double)GeneralConstants.ViewActivationDelay / 2), Scheduler)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(isActive =>
                {
                    if (isActive)
                    {
                        try
                        {
                            _ExportManager.GetCommonJournalExportProgress();

                            _SchedulerProvider.MainThread.Schedule(() => ExportManagerIsExecuted = true);
                        }
                        catch (Exception)
                        {
                            _SchedulerProvider.MainThread.Schedule(() => ExportManagerIsExecuted = false);
                        }


                        if (DataSourceMode.RealTime == JournalMode)
                        {
                            SubscribeToDSSEvents(new[]
                            {
                                DSSEventType.DSSLayerChanged, DSSEventType.DSSConfigurationFullChanged
                            });
                        }
                        else
                        {
                            _SchedulerProvider.MainThread.Schedule(() => BusyIndicatorState = false);
                        }
                    }
                });

            #endregion

            #region ExportManagerIsExecuted

            this.ObservableForProperty(vm => vm.ExportManagerIsExecuted)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(exportManagerIsExecuted =>
                {
                    if (exportManagerIsExecuted)
                    {
                        ExportIsChecked = true;
                    }
                });

            #endregion

            #region FilterObjectsIsChecked

            this.ObservableForProperty(vm => vm.FilterObjectsIsChecked)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(filterObjectsIsChecked =>
                {
                    RefreshObjects();

                    ApplyFilterByObjectsCommandIsEnabled = _Model.FilterObjectsIDs.Any();
                    FilterByObjectsIsEnabled = _Model.FilterObjectsIDs.Any();

                    FiltersIsChanged = true;
                });

            #endregion

            #region Событие изменения режима журнала

            this.ObservableForProperty(vm => vm.JournalMode)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(journalMode =>
                {
                    if (DataSourceMode.Archive == journalMode)
                    {
                        SetAndWaitingNewLayerProcessing();

                        if (null != _Model.FilterDateTimeStart && null != _Model.FilterDateTimeEnd)
                        {
                            DateTimePickerStart = (DateTime)_Model.FilterDateTimeStart;
                            DateTimePickerEnd = (DateTime)_Model.FilterDateTimeEnd;
                        }
                        else
                        {
                            DateTimePickerStart = DateTime.MinValue == DSSHelper.CurrentLayerTimestamp
                                ? DateTime.Now.AddHours(-DefaultStartDateTimeInHours)
                                : DSSHelper.CurrentLayerTimestamp.AddHours(
                                    -DefaultStartDateTimeInHours);
                            DateTimePickerEnd = DateTime.MinValue == DSSHelper.CurrentLayerTimestamp
                                ? DateTime.Now
                                : DSSHelper.CurrentLayerTimestamp;
                        }

                        ResetNewLayerProcessing();
                    }
                    else
                    {
                        SetAndWaitingNewLayerProcessing();

                        FiltersIsChanged = true;

                        ResetNewLayerProcessing();
                    }
                });

            this.ObservableForProperty(vm => vm.JournalMode)
                .Throttle(TimeSpan.FromMilliseconds(750), Scheduler)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(journalMode =>
                {
                    if (DataSourceMode.RealTime == journalMode)
                    {
                        SubscribeToDSSEvents(new[]
                        {
                            DSSEventType.DSSLayerChanged, DSSEventType.DSSConfigurationFullChanged
                        });
                    }
                    else
                    {
                        UnsubscribeFromDSSEvents(new[]
                        {
                            DSSEventType.DSSLayerChanged, DSSEventType.DSSConfigurationFullChanged
                        });
                    }
                });

            #endregion

            #region Событие изменения даты начала/конца

            this.ObservableForProperty(vm => vm.DateTimePickerStart)
                .Merge(this.ObservableForProperty(vm => vm.DateTimePickerEnd))
                .Subscribe(c =>
                {
                    if (DataSourceMode.Archive == JournalMode)
                    {
                        FilterDateTimeIsEnabled = ViewModelFiltersIsValid;
                    }
                });

            #endregion

            #region Событие изменения фильтров

            Observable.Merge(
                this.ObservableForProperty(x => x.FilterEntryTypeSystemStateIsChecked).Select(x => Unit.Default),
                this.ObservableForProperty(x => x.FilterEntryTypeLeakageDetectionIsChecked).Select(x => Unit.Default),
                this.ObservableForProperty(x => x.FilterEntryTypeHeadPressureControlIsChecked).Select(x => Unit.Default),
                this.ObservableForProperty(x => x.FilterEntryTypePIGControlIsChecked).Select(x => Unit.Default),
                this.ObservableForProperty(x => x.FilterEntryTypeSystemMessagesIsChecked).Select(x => Unit.Default),
                this.ObservableForProperty(x => x.FilterEntryTypeUserActionsIsChecked).Select(x => Unit.Default)
                ).ObserveOn(Scheduler)
                .Subscribe(x =>
                {
                    SetAndWaitingNewLayerProcessing();

                    RefreshFilterEntryTypes();

                    _SchedulerProvider.MainThread.Schedule(() => FiltersIsChanged = true);

                    ResetNewLayerProcessing();
                });

            #endregion

            #region Событие открытия/закрытия окна объектов

            this.ObservableForProperty(vm => vm.ObjectsListIsDropDownOpen)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(objectsListIsDropDownOpen =>
                {
                    if (!objectsListIsDropDownOpen)
                    {
                        FilterByObjectsList.SuspendNotifications();

                        foreach (CategoryViewModel o in FilterByObjectsList)
                        {
                            foreach (CategoryViewModel sc in o.SubCategories)
                            {
                                sc.IsChecked = _Model.FilterObjectsIDs.Contains(sc.Id);
                            }
                        }

                        FilterByObjectsList.ResumeNotifications();

                        FilterByObjectsIsEnabled = _Model.FilterObjectsIDs.Any();
                    }
                    else
                    {
                        if (FilterObjectsIsChecked)
                        {
                            ApplyFilterByObjectsCommandIsEnabled = false;
                            FilterByObjectsIsEnabled = _Model.FilterObjectsIDs.Any();
                            ResetFilterObjectsCommandIsEnabled = _Model.FilterObjectsIDs.Any();
                        }
                        else if (SelectedObjectsList.Any() && !_Model.FilterObjectsIDs.Any())
                        {
                            ApplyFilterByObjectsCommandIsEnabled = true;
                            FilterByObjectsIsEnabled = _Model.FilterObjectsIDs.Any();
                        }
                    }
                });

            #endregion

            #region FilterColorsIsChecked

            this.ObservableForProperty(vm => vm.FilterColorsIsChecked)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(filterColorsIsChecked =>
                {
                    _SchedulerProvider.MainThread.Schedule(() =>
                    {
                        RefreshColors();

                        ApplyFilterByColorsCommandIsEnabled = _Model.FilterColorsMessageTypes.Any();
                        FilterByColorsIsEnabled = _Model.FilterColorsMessageTypes.Any();

                        FiltersIsChanged = true;
                    });
                });

            #endregion

            #region Событие открытия/закрытия окна цветов

            this.ObservableForProperty(vm => vm.ColorsListIsDropDownOpen)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(colorsListIsDropDownOpen =>
                {
                    if (!colorsListIsDropDownOpen)
                    {
                        FilterByColorsIsEnabled = FilterByColorsList.Items.Any(e => e.IsChecked);
                    }
                    else
                    {
                        if (FilterColorsIsChecked)
                        {
                            ApplyFilterByColorsCommandIsEnabled = false;
                            FilterByColorsIsEnabled = _Model.FilterColorsMessageTypes.Any();
                            ResetFilterColorsCommandIsEnabled = _Model.FilterColorsMessageTypes.Any();
                        }
                        else if (SelectedColorsList.Items.Any() && !_Model.FilterColorsMessageTypes.Any())
                        {
                            ApplyFilterByColorsCommandIsEnabled = true;
                            FilterByColorsIsEnabled = _Model.FilterColorsMessageTypes.Any();
                        }
                    }
                });

            #endregion

            #region FiltersIsChanged

            this.ObservableForProperty(vm => vm.FiltersIsChanged)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(filtersIsChanged => _SchedulerProvider.MainThread.Schedule(() => ApplyFilterIsProcessing = filtersIsChanged));

            #endregion

            #region ApplyFilterIsProcessing

            this.ObservableForProperty(vm => vm.ApplyFilterIsProcessing)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(applyFilterIsProcessing =>
                {
                    ExportIsEnabled = false;

                    if (applyFilterIsProcessing)
                    {
                        ObjectsListIsDropDownOpen = false;
                        ColorsListIsDropDownOpen = false;

                        BusyIndicatorMessage = "Применение фильтра...";
                        BusyIndicatorIsIndeterminate = true;
                        BusyIndicatorState = true;

                        _Model.AppliedFilterDescriptions = GetEventsFilterDescriptions(_Model, JournalMode, FilterByMessageText);

                        RefreshFilterEntryTypes();

                        if (DataSourceMode.Archive == JournalMode)
                        {
                            _Model.FilterDateTimeStart = DateTimePickerStart;
                            _Model.FilterDateTimeEnd = DateTimePickerEnd;

                            if (!_loadEntriesFromArchiveWorker.IsBusy)
                                _loadEntriesFromArchiveWorker.RunWorkerAsync();
                        }
                        else
                        {
                            SetAndWaitingNewLayerProcessing();

                            CreateAllEntries(() =>
                            {
                                ResetNewLayerProcessing();
                            });
                        }
                    }
                    else
                    {
                        _SchedulerProvider.MainThread.Schedule(() =>
                        {
                            BusyIndicatorState = false;
                            BusyIndicatorMessage = "Обновление данных...";
                        });
                    }
                });

            #endregion

            #region ExportIsChecked

            this.ObservableForProperty(vm => vm.ExportIsChecked)
                .ObserveOn(Scheduler)
                .Value()
                .Where(x => !x)
                .Subscribe(_ =>
                {
                    if (_ResetExportIsCheckedFromDialog)
                    {
                        _ResetExportIsCheckedFromDialog = false;

                        return;
                    }

                    if (_ExportEntriesWorker.IsBusy)
                        _ExportEntriesWorker.CancelAsync();

                    ExportProgress = 0;
                });

            // Экспорт запускаем с небольшой задержкой, после открытия панели экспорта (200мс)
            this.ObservableForProperty(vm => vm.ExportIsChecked)
                .Throttle(TimeSpan.FromMilliseconds(300), Scheduler)
                .ObserveOn(Scheduler)
                .Value()
                .Where(x => x)
                .Subscribe(_ => StartExportToFile());

            #endregion

            #region FilterEntryTypeIsChecked

            this.ObservableForProperty(vm => vm.FilterEntryTypeIsChecked)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(filterEntryTypeIsChecked => _Model.FilterSourceMaskEnabled = filterEntryTypeIsChecked);

            #endregion

            #region _ModuleSettings

            this.ObservableForProperty(vm => vm._ModuleSettings)
                .ObserveOn(Scheduler)
                .Select(x => x.GetValue())
                .Subscribe(moduleSettings => RefreshFilterByColorsList(moduleSettings));

            #endregion

            #region FilterByColorsList.ItemChanged

            FilterByColorsList.Connect()
                .WhenPropertyChanged(x => x.IsChecked)
                .ObserveOn(Scheduler)
                .Subscribe(changedItem =>
                {
                    JournalColorGroupViewModel colorGroup = changedItem.Sender;
                    var isChecked = (bool)changedItem.Value;
                    List<int> messages = colorGroup.Messages.Items.Select(e => e.MType).ToList();

                    _SchedulerProvider.MainThread.Schedule(() =>
                    {
                        if (isChecked)
                        {
                            SourceListEditConvenienceEx.AddRange(SelectedColorsList, messages);
                        }
                        else
                        {
                            IEnumerable<int> itemsToRemove = SelectedColorsList.Items.Where(e => messages.Any(m => m == e));

                            SelectedColorsList.RemoveMany(itemsToRemove);
                        }

                        bool listIsEqual = SelectedColorsList.Items.SequenceEqual(_Model.FilterColorsMessageTypes);

                        FilterByColorsIsEnabled = !listIsEqual;
                        ApplyFilterByColorsCommandIsEnabled = !listIsEqual;
                        ResetFilterColorsCommandIsEnabled = !listIsEqual;
                    });
                });

            #endregion

            #region FilterByMessageText

            this.ObservableForProperty(vm => vm.FilterByMessageText)
                .ObserveOn(Scheduler)
                .Value()
                .Throttle(TimeSpan.FromSeconds(1))
                .Subscribe(stringToSearch => FilterByMessageTextExecute(stringToSearch));

            #endregion
        }

        private void RefreshFilterByColorsList(JournalModuleSettingsViewModel moduleSettings)
        {
            _SchedulerProvider.MainThread.Schedule(() =>
            {
                ColorsListIsDropDownOpen = false;

                FilterByColorsList.Clear();
                SourceListEditConvenienceEx.AddRange(FilterByColorsList, _Factory.CreateJournalColorGroups(moduleSettings.ColorGroupsBindable));

                FilterColorsListButtonIsEnabled = FilterByColorsList.Items.Any();

                RefreshColors();
            });
        }

        /// <summary>
        ///     Обработчик изменения коллекции объектов в окне фильтрации по объектам
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void FilterObjectsListOnCollectionChanged(object sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.Action == NotifyCollectionChangedAction.Reset)
            {
                var objects = sender as IEnumerable<CategoryViewModel>;

                if (null == objects)
                    return;

                foreach (CategoryViewModel item in objects)
                {
                    foreach (CategoryViewModel subCategory in item.SubCategories)
                    {
                        subCategory.PropertyChanged += ObjectEntryInFilterObjectsListPropertyChanged;
                    }

                    item.PropertyChanged += ObjectEntryInFilterObjectsListPropertyChanged;
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Remove)
            {
                // Удаление записи
                foreach (CategoryViewModel item in e.OldItems)
                {
                    foreach (CategoryViewModel subCategory in item.SubCategories)
                    {
                        subCategory.PropertyChanged -= ObjectEntryInFilterObjectsListPropertyChanged;
                    }

                    item.PropertyChanged -= ObjectEntryInFilterObjectsListPropertyChanged;
                }
            }
            else if (e.Action == NotifyCollectionChangedAction.Add)
            {
                // Добавление записи
                foreach (CategoryViewModel item in e.NewItems)
                {
                    foreach (CategoryViewModel subCategory in item.SubCategories)
                    {
                        subCategory.PropertyChanged += ObjectEntryInFilterObjectsListPropertyChanged;
                    }

                    item.PropertyChanged += ObjectEntryInFilterObjectsListPropertyChanged;
                }
            }
        }

        /// <summary>
        ///     Обработчик изменения записи в коллекции объектов в окне фильтрации по объектам
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ObjectEntryInFilterObjectsListPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            try
            {
                var objectEntry = sender as CategoryViewModel;

                if (null == objectEntry)
                    return;

                if ("IsChecked" == e.PropertyName)
                {
                    // Рассматриваем только записи, в которых нет подкатегорий
                    if (objectEntry.SubCategories.Any())
                        return;

                    if (false == objectEntry.IsChecked)
                        SelectedObjectsList.Remove(objectEntry.Id);
                    else if (true == objectEntry.IsChecked)
                        SelectedObjectsList.Add(objectEntry.Id);

                    bool listIsEqual = SelectedObjectsList.SequenceEqual(_Model.FilterObjectsIDs);

                    FilterByObjectsIsEnabled = !listIsEqual;
                    ApplyFilterByObjectsCommandIsEnabled = !listIsEqual;
                    ResetFilterObjectsCommandIsEnabled = !listIsEqual;
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        /// <summary>
        ///     Обновление фильтра по типу сообщений
        /// </summary>
        private void RefreshFilterEntryTypes()
        {
            var eventSourceFilters = new List<EventSourceMaskType>();

            _Model.FilterSourceMask.Clear();

            if (FilterEntryTypeIsChecked)
            {
                if (FilterEntryTypeSystemStateIsChecked)
                    eventSourceFilters.Add(EventSourceMaskType.SystemState);

                if (FilterEntryTypeLeakageDetectionIsChecked)
                    eventSourceFilters.Add(EventSourceMaskType.LeakageDetection);

                if (FilterEntryTypeHeadPressureControlIsChecked)
                    eventSourceFilters.Add(EventSourceMaskType.HeadPressure);

                if (FilterEntryTypePIGControlIsChecked)
                    eventSourceFilters.Add(EventSourceMaskType.PIG);

                if (FilterEntryTypeSystemMessagesIsChecked)
                    eventSourceFilters.Add(EventSourceMaskType.SystemMessages);

                if (FilterEntryTypeUserActionsIsChecked)
                    eventSourceFilters.Add(EventSourceMaskType.UserActions);

                _Model.FilterSourceMask.AddRange(eventSourceFilters);
            }
        }

        private static EventsFilterDescriptions GetEventsFilterDescriptions(CommonJournalModel model, DataSourceMode journalMode, string filterByMessageText)
        {
            var descriptions = new EventsFilterDescriptions();

            if (DataSourceMode.Archive == journalMode && null != model.FilterDateTimeStart &&
                null != model.FilterDateTimeEnd)
            {
                descriptions.DateTimeStart = model.FilterDateTimeStart;
                descriptions.DateTimeEnd = model.FilterDateTimeEnd;
            }

            descriptions.SourceMask = model.FilterSourceMask;
            descriptions.ObjectIds = model.FilterObjectsIDs;

            if (null == model.FilterTextMessageTypes)
            {
                descriptions.Sources = null;
            }
            else
            {
                descriptions.Sources = new List<int>();

                if (model.FilterTextMessageTypes.Any())
                {
                    if (model.FilterColorsMessageTypes.Any())
                    {
                        List<int> intersect = model.FilterColorsMessageTypes.Intersect(model.FilterTextMessageTypes).ToList();

                        if (intersect.Any())
                        {
                            descriptions.Sources.AddRange(intersect);
                        }
                        else
                        {
                            descriptions.Sources = null;
                        }
                    }
                    else
                    {
                        descriptions.Sources.AddRange(model.FilterTextMessageTypes);
                    }
                }
                else
                {
                    descriptions.Sources.AddRange(model.FilterColorsMessageTypes);
                }
            }

            if (descriptions.SourceMask.Any(sm => sm == EventSourceMaskType.UserActions) && !string.IsNullOrEmpty(filterByMessageText))
            {
                descriptions.Message = filterByMessageText;
                descriptions.MessageFilterCondition = StringFilterCondition.Contains;
            }

            return descriptions;
        }

        private bool FilterObjectsListViewContains(object obj)
        {
            if (string.IsNullOrEmpty(FilterByObjectsSelectedString))
                return true;

            var vm = obj as CategoryViewModel;

            return null != vm && vm.SubCategoriesView.Count > 0;
        }

        #region Workers

        #region ExportEntriesWorker

        private ILDSServicesConnector _ConnectedConnector;

        private void StartExportToFile()
        {
            _ConnectedConnector = _ServicesConnectors.Connectors
                .Select(e => e.Value)
                .FirstOrDefault(e => MessageReceiverState.Enabled == e.RealtimeReceiversState);

            if (null == _ConnectedConnector)
            {
                Logger.Log("Экспорт общего журнала невозможен, нет подключенных коннекторов", Category.Exception, Priority.None);

                return;
            }

            FileType selectedFileType = DefaultExportFileTypes.First();

            string fileName = "Безымянный";

            FileDialogResult dialogResult = null;

            var mre = new ManualResetEvent(false);

            _SchedulerProvider.MainThread.Schedule(() =>
            {
                dialogResult = _FileDialogService.ShowSaveFileDialog(null, DefaultExportFileTypes, selectedFileType, fileName, "Экспорт общего журнала в файл");

                if (dialogResult.IsValid)
                    fileName = dialogResult.FileName;

                mre.Set();
            });

            mre.WaitOne();

            if (null == dialogResult || !dialogResult.IsValid || string.IsNullOrWhiteSpace(fileName) || _ExportEntriesWorker.IsBusy)
            {
                _SchedulerProvider.MainThread.Schedule(() =>
                {
                    _ResetExportIsCheckedFromDialog = true;
                    ExportIsChecked = false;
                    _ConnectedConnector = null;
                });

                return;
            }

            EventsFilterDescriptions filterDescriptors = GetEventsFilterDescriptions(_Model, JournalMode, FilterByMessageText);

            EventsSortDescriptions sortDescriptors = RHDAExtensions.GetEventsSortDescriptions(new SortDescriptionCollection
            {
                ModuleConstants.DefaultTimeStampSortDescription,
            });

            IBackupConnectionSettings settings = _BackupSettings.Servers.First(e => e.Key == _ConnectedConnector.Priority).Value;

            Logger.Log(string.Format("Экспорт общего журнала - используется коннектор #{0}, RHDA url {1}", _ConnectedConnector.Priority, settings.RHDAConnection), Category.Info,
                       Priority.None);

            var exportParameters = new JournalExportParameters
            {
                TableName = JournalTables.Entries.ToString(),
                OutputFileName = fileName,
                FilterDescriptions = filterDescriptors,
                SortDescriptions = sortDescriptors,
                Configuration = DSSHelper.Configuration,
                EUSettings = EUSettingsModel.Create(EUSettings),
                EUNumericFormat = _NumericFormat,
                RHDATimeout = _BackupSettings.RHDATimeout,
                RHDAIPEndPoints = settings.RHDAConnection.EndPoints,
                JournalInfoProvider = new CommonJournalInfoProvider(),
            };

            _ExportEntriesWorker.RunWorkerAsync(exportParameters);
        }

        private static readonly List<FileType> DefaultExportFileTypes = new List<FileType>
        {
            new FileType("CSV", ".csv"),
        };

        private readonly BackgroundWorker _ExportEntriesWorker = new BackgroundWorker();

        private void ExportEntriesWorkerOnDoWork(object sender, DoWorkEventArgs eventArgs)
        {
            var backgroundWorker = sender as BackgroundWorker;

            if (null == backgroundWorker)
                return;

            var exportParameters = eventArgs.Argument as JournalExportParameters;

            if (null == exportParameters)
                return;

            int percentOfComplete = 0;

            try
            {
                Logger.Log("Запуск экспорта общего журнала.", Category.Info, Priority.None);

                backgroundWorker.ReportProgress(0);

                #region Проверка наличия процесса экспорта в памяти

                IEnumerable<Process> allProcesses = Process.GetProcesses().Where(e => e.ProcessName == ExportManagerProcessName);

                if (allProcesses.Any())
                {
                    Logger.Log(string.Format("Процесс {0} уже выполняется.", ExportManagerProcessName), Category.Info, Priority.None);
                }
                else
                {
                    Logger.Log(string.Format("Процесс {0} не обнаружен.", ExportManagerProcessName), Category.Info, Priority.None);
                    Logger.Log(string.Format("Экспорт общего журнала в файл {0}.", exportParameters.OutputFileName), Category.Info, Priority.None);

                    ProcessHelper.RunProcess(Logger, string.Format("{0}.exe", ExportManagerProcessName), new string[]
                    {
                    });

                    Thread.Sleep(3000);

                    _ExportManager.StartCommonJournalExport(exportParameters);
                }

                #endregion

                do
                {
                    if (backgroundWorker.CancellationPending)
                    {
                        Logger.Log("Отмена экспорта.", Category.Info, Priority.None);

                        eventArgs.Cancel = true;

                        ProcessHelper.KillProcess(Logger, ExportManagerProcessName);

                        percentOfComplete = 100;

                        return;
                    }

                    Thread.Sleep(100);

                    percentOfComplete = _ExportManager.GetCommonJournalExportProgress();

                    backgroundWorker.ReportProgress(percentOfComplete);
                } while (percentOfComplete < 100);

                _SchedulerProvider.MainThread.Schedule(() => ExportIsChecked = false);

                Thread.Sleep(100);
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }

            if (percentOfComplete < 100)
            {
                bool rhdaIsAvailable = false;

                try
                {
                    string msg = _ConnectedConnector.RHDA.Ping();

                    rhdaIsAvailable = true;
                }
                catch (Exception)
                {
                }

                Notification notification;

                if (rhdaIsAvailable)
                {
                    notification = new Notification
                    {
                        Title = "Экспорт общего журнала",
                        Content = "При экспорте журнала произошла ошибка. Журнал экспортирован не полностью.",
                    };
                }
                else
                {
                    notification = new Notification
                    {
                        Title = "Экспорт общего журнала",
                        Content = "При экспорте журнала произошла ошибка. Отключение RHDA. Журнал экспортирован не полностью.",
                    };
                }

                _SchedulerProvider.MainThread.Schedule(() => ExportFailedRequest.Raise(notification, _ => ExportIsChecked = false));
            }
        }

        private const string ExportManagerProcessName = "zzz.ExportManager";

        private void ExportEntriesWorkerOnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
        }

        private void ExportEntriesWorkerOnProgressChanged(object sender, ProgressChangedEventArgs e)
        {   // if(ProgressPercentage > 0)
            ExportProgressMessage = string.Format("Экспорт журнала: выполнено {0}%", e.ProgressPercentage);
            Logger.Log(ExportProgressMessage, Category.Debug, Priority.None);

            _SchedulerProvider.MainThread.Schedule(() => ExportProgress = e.ProgressPercentage);
        }

        #endregion

        #region LoadEntriesFromArchiveWorker

        private readonly BackgroundWorker _loadEntriesFromArchiveWorker = new BackgroundWorker();

        private void LoadEntriesFromArchiveWorkerOnDoWork(object sender, DoWorkEventArgs eventArgs)
        {
            try
            {
                _SchedulerProvider.MainThread.Schedule(() => ExportIsEnabled = false);

                CreateAllEntries(() =>
                {
                });
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        public bool RetryUntilSuccessOrTimeout(Func<bool> task, TimeSpan timeSpan)
        {
            bool success = false;
            int elapsed = 0;

            while ((!success) && (elapsed < timeSpan.TotalMilliseconds))
            {
                Thread.Sleep(100);
                elapsed += 100;
                success = task();
            }

            return success;
        }

        private void LoadEntriesFromArchiveWorkerOnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if (RetryUntilSuccessOrTimeout(() => null != AllEntries && !AllEntries.IsInitializing,
                                           TimeSpan.FromSeconds(100)))
            {
                _SchedulerProvider.MainThread.Schedule(() => ExportIsEnabled = AllEntries.Count > 0);

                return;
            }

            Logger.Log("Журнал не был инициализирован за отведенное время.", Category.Exception, Priority.Medium);
        }

        #endregion

        #region FilterObjectsLoadingWorker

        private readonly BackgroundWorker _FilterObjectsLoadingWorker = new BackgroundWorker();

        private void FilterObjectsLoadingWorkerOnDoWork(object sender, DoWorkEventArgs eventArgs)
        {
            try
            {
                _SchedulerProvider.MainThread.Schedule(() =>
                {
                    FilterByObjectsListIsLoading = true;
                    FilterByObjectsList.SuspendNotifications();
                    FilterByObjectsList.Clear();
                    FilterByObjectsList.AddCategories(DSSHelper);
                });
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        private void FilterObjectsLoadingWorkerOnRunWorkerCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            FilterByObjectsList.ResumeNotifications();
            FilterObjectsListView.Refresh();
            FilterByObjectsListIsLoading = false;
        }

        #endregion

        #endregion

        #region Commands

        #region JournalModeSelectorLabelClick

        public DelegateCommand JournalModeSelectorLabelClick
        {
            get { return new DelegateCommand(o => JournalModeSelectorLabelClickExecute(o)); }
        }

        private void JournalModeSelectorLabelClickExecute(object o)
        {
            if (o is DataSourceMode)
                JournalMode = (DataSourceMode)o;
        }

        #endregion

        #region FilterEntryTypeClick

        public DelegateCommand FilterEntryTypeClick
        {
            get { return new DelegateCommand(o => FilterEntryTypeClickExecute(o)); }
        }

        private void FilterEntryTypeClickExecute(object o)
        {
            SetAndWaitingNewLayerProcessing();

            if (null != o)
                FilterEntryTypeIsChecked = !FilterEntryTypeIsChecked;

            _Model.FilterSourceMask.Clear();

            if (FilterEntryTypeIsChecked)
            {
                // TODO: Проверить корректность данной проверки
                if (!FilterEntryTypeSystemStateIsChecked && !FilterEntryTypeLeakageDetectionIsChecked &&
                    !FilterEntryTypeHeadPressureControlIsChecked && !FilterEntryTypePIGControlIsChecked)
                {
                    ResetNewLayerProcessing();

                    return;
                }
            }

            RefreshFilterEntryTypes();

            FiltersIsChanged = true;

            ResetNewLayerProcessing();
        }

        #endregion

        #region ResetFilterObjects

        public ICommand ResetFilterObjects
        {
            get { return new DelegateCommand(o => ResetFilterObjectsExecute(o)); }
        }

        private void ResetFilterObjectsExecute(object o)
        {
            SetAndWaitingNewLayerProcessing();

            ResetFilterObjectsApply();

            ResetNewLayerProcessing();
        }

        private void ResetFilterObjectsApply()
        {
            FilterByObjectsList.SuspendNotifications();

            foreach (CategoryViewModel c in FilterByObjectsList)
            {
                c.IsChecked = false;
            }

            FilterByObjectsList.ResumeNotifications();

            FilterByObjectsSelectedString = string.Empty;

            ResetFilterObjectsCommandIsEnabled = false;

            FilterObjectsIsChecked = false;
        }

        #endregion

        #region ResetFilterColors

        private DelegateCommand _ResetFilterColors;

        public DelegateCommand ResetFilterColors
        {
            get { return _ResetFilterColors ?? (_ResetFilterColors = new DelegateCommand(o => ResetFilterColorsExecute(o))); }
        }

        private void ResetFilterColorsExecute(object o)
        {
            SetAndWaitingNewLayerProcessing();

            ResetFilterColorsApply();

            ResetNewLayerProcessing();
        }

        private void ResetFilterColorsApply()
        {
            foreach (JournalColorGroupViewModel c in FilterByColorsList.Items)
            {
                c.IsChecked = false;
            }

            SelectedColorsList.Clear();

            AppliedFilterByColorsList.Clear();

            ResetFilterColorsCommandIsEnabled = false;

            FilterColorsIsChecked = false;
        }

        #endregion

        private void RefreshColors()
        {
            try
            {
                _Model.FilterColorsMessageTypes.Clear();
                SelectedColorsList.Clear();

                foreach (JournalColorGroupViewModel c in FilterByColorsList.Items.Where(e => e.IsChecked).OrderBy(e => e.Name))
                {
                    List<int> range = c.Messages.Items.Select(e => e.MType).ToList();

                    if (FilterColorsIsChecked)
                        _Model.FilterColorsMessageTypes.AddRange(range);

                    SourceListEditConvenienceEx.AddRange(SelectedColorsList, range);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        #region EnableFilterObjectsClick

        public DelegateCommand ApplyFilterObjectsClick
        {
            get { return new DelegateCommand(o => ApplyFilterObjectsClickExecute(o)); }
        }

        private void ApplyFilterObjectsClickExecute(object o)
        {
            SetAndWaitingNewLayerProcessing();

            // При выключенном фильтре и нажатии кнопки "Применить"
            if (!FilterObjectsIsChecked && null == o)
            {
                FilterByObjectsIsEnabled = true;
                ApplyFilterByObjectsCommandIsEnabled = true;
                FilterObjectsIsChecked = true;
            }
            // При включенном фильтре и нажатии кнопки "Применить"
            else if (FilterObjectsIsChecked && null == o)
            {
                ApplyFilterByObjectsCommandIsEnabled = false;

                if (!SelectedObjectsList.Any())
                {
                    ResetFilterObjectsApply();

                    FiltersIsChanged = true;
                    FilterByObjectsIsEnabled = false;
                    ApplyFilterByObjectsCommandIsEnabled = false;
                }
                else
                {
                    RefreshObjects();

                    FiltersIsChanged = true;
                    FilterByObjectsIsEnabled = SelectedObjectsList.Any();
                    ApplyFilterByObjectsCommandIsEnabled = SelectedObjectsList.Any();
                }
            }
            // При выключенном фильтре и нажатии checkbox
            else if (!FilterObjectsIsChecked && null != o)
            {
                ApplyFilterByObjectsCommandIsEnabled = false;
                FilterByObjectsIsEnabled = true;
                FilterObjectsIsChecked = true;
            }
            // При включенном фильтре и нажатии checkbox
            else if (FilterObjectsIsChecked && null != o)
            {
                ApplyFilterByObjectsCommandIsEnabled = false;
                FilterObjectsIsChecked = false;

                if (!SelectedObjectsList.Any())
                {
                    ResetFilterObjectsApply();

                    FiltersIsChanged = true;
                    FilterByObjectsIsEnabled = false;
                }
                else
                {
                    RefreshObjects();

                    FiltersIsChanged = true;
                    FilterByObjectsIsEnabled = SelectedObjectsList.Any();
                }
            }

            ResetNewLayerProcessing();
        }

        private void RefreshObjects()
        {
            try
            {
                _Model.FilterObjectsIDs.Clear();
                SelectedObjectsList.Clear();

                foreach (CategoryViewModel objects in FilterByObjectsList)
                {
                    List<int> range =
                        objects.SubCategories.Where(e => true == e.IsChecked)
                            .OrderBy(e => e.Id)
                            .Select(e => e.Id)
                            .ToList();

                    if (FilterObjectsIsChecked)
                        _Model.FilterObjectsIDs.AddRange(range);

                    SelectedObjectsList.AddRange(range);
                }
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);
            }
        }

        #endregion

        #region EnableFilterColorsClick

        public DelegateCommand ApplyFilterColorsClick
        {
            get { return new DelegateCommand(o => ApplyFilterColorsClickExecute(o)); }
        }

        private void ApplyFilterColorsClickExecute(object o)
        {
            SetAndWaitingNewLayerProcessing();

            AppliedFilterByColorsList.Clear();
            SourceListEditConvenienceEx.AddRange(AppliedFilterByColorsList, FilterByColorsList.Items.Where(e => e.IsChecked));

            // При выключенном фильтре и нажатии кнопки "Применить"
            if (!FilterColorsIsChecked && null == o)
            {
                FilterByColorsIsEnabled = true;
                ApplyFilterByColorsCommandIsEnabled = true;
                FilterColorsIsChecked = true;
            }
            // При включенном фильтре и нажатии кнопки "Применить"
            else if (FilterColorsIsChecked && null == o)
            {
                ApplyFilterByColorsCommandIsEnabled = false;

                if (!SelectedColorsList.Items.Any())
                {
                    ResetFilterColorsApply();

                    FiltersIsChanged = true;
                    FilterByColorsIsEnabled = false;
                    ApplyFilterByColorsCommandIsEnabled = false;
                }
                else
                {
                    RefreshColors();

                    FiltersIsChanged = true;
                    FilterByColorsIsEnabled = SelectedColorsList.Items.Any();
                    ApplyFilterByColorsCommandIsEnabled = SelectedColorsList.Items.Any();
                }
            }
            // При выключенном фильтре и нажатии checkbox
            else if (!FilterColorsIsChecked && null != o)
            {
                ApplyFilterByColorsCommandIsEnabled = false;
                FilterByColorsIsEnabled = true;
                FilterColorsIsChecked = true;
            }
            // При включенном фильтре и нажатии checkbox
            else if (FilterColorsIsChecked && null != o)
            {
                ApplyFilterByColorsCommandIsEnabled = false;
                FilterColorsIsChecked = false;

                if (!SelectedColorsList.Items.Any())
                {
                    ResetFilterColorsApply();

                    FiltersIsChanged = true;
                    FilterByColorsIsEnabled = false;
                }
                else
                {
                    RefreshColors();

                    FiltersIsChanged = true;
                    FilterByColorsIsEnabled = SelectedColorsList.Items.Any();
                }
            }

            ResetNewLayerProcessing();
        }

        #endregion

        #region SearchInFilterObjects

        public DelegateCommand SearchInFilterObjects
        {
            get { return new DelegateCommand(o => SearchInFilterObjectsExecute(o)); }
        }

        private void SearchInFilterObjectsExecute(object o)
        {
            var eventArgs = o as RoutedEventArgs;

            if (null == eventArgs)
                return;

            var searchTextBox = eventArgs.Source as SearchTextBox;

            if (null == searchTextBox)
                return;

            FilterByObjectsListIsLoading = true;

            FilterByObjectsList.SuspendNotifications();

            foreach (CategoryViewModel filterObject in FilterByObjectsList)
            {
                filterObject.FilterString = searchTextBox.Text;
            }

            FilterByObjectsList.ResumeNotifications();

            FilterByObjectsSelectedString = searchTextBox.Text;
            FilterObjectsListView.Refresh();

            FilterByObjectsListIsLoading = false;
        }

        #endregion

        #region FilterDateTimeApply

        public DelegateCommand FilterDateTimeApply
        {
            get { return new DelegateCommand(o => FilterDateTimeApplyExecute(o)); }
        }

        private void FilterDateTimeApplyExecute(object o)
        {
            SetAndWaitingNewLayerProcessing();

            FiltersIsChanged = true;

            ResetNewLayerProcessing();
        }

        #endregion

        #region EventsExportCommand

        public DelegateCommand EventsExport
        {
            get { return new DelegateCommand(o => EventsExportExecute(o), o => ExportIsEnabled); }
        }

        private void EventsExportExecute(object o)
        {
            ExportIsChecked = true;

            EventsExport.InvalidateCanExecute();
        }

        #endregion

        #region CancelExport

        public DelegateCommand CancelExport
        {
            get { return new DelegateCommand(o => CancelExportExecute(o), o => true); }
        }

        private void CancelExportExecute(object o)
        {
            ExportIsChecked = false;

            EventsExport.InvalidateCanExecute();
        }

        #endregion

        #region CanEventsExport

        private bool? _CanEventsExport;

        public bool? CanEventsExport
        {
            get { return _CanEventsExport ?? (_CanEventsExport = true); }
            set { this.RaiseAndSetIfChanged(ref _CanEventsExport, value); }
        }

        #endregion

        #region AllEntriesHasMoreItems

        public DelegateCommand AllEntriesHasMoreItems
        {
            get { return new DelegateCommand(o => AllEntriesHasMoreItemsExecute(o), o => true); }
        }

        private void AllEntriesHasMoreItemsExecute(object o)
        {
            SetAndWaitingNewLayerProcessing();

            //CreateAllEntries();
            AllEntries.LoadMoreItems();

            ResetNewLayerProcessing();
        }

        #endregion

        #region FilterByMessageText

        private void FilterByMessageTextExecute(object o)
        {
            var textToSearch = (string)o;

            try
            {
                SetAndWaitingNewLayerProcessing();

                if (string.IsNullOrWhiteSpace(textToSearch) || FilterEntryTypeUserActionsIsChecked && FilterEntryTypeIsChecked)
                {
                    _Model.FilterTextMessageTypes = new List<int>();
                }
                else
                {
                    var filteredMessages = ResourceHelper.MessageFormats
                                                         .Where(e => e.Value.Contains(textToSearch, StringComparison.InvariantCultureIgnoreCase))
                                                         .Select(x => x.Key)
                                                         .ToList();

                    _Model.FilterTextMessageTypes = filteredMessages.Count != 0 
                        ? filteredMessages 
                        : null;
                }

                CreateAllEntries(() =>
                {
                    ResetNewLayerProcessing();
                });
            }
            catch (Exception ex)
            {
                Logger.LogException(ex);

                ResetNewLayerProcessing();
            }
        }

        #endregion

        #endregion

        #region Properties

        #region SelectedObjectsList

        private List<int> _SelectedObjectsList = new List<int>();

        /// <summary>
        ///     Список выделенных объектов в окне
        /// </summary>
        public List<int> SelectedObjectsList
        {
            get { return _SelectedObjectsList; }
            set { this.RaiseAndSetIfChanged(ref _SelectedObjectsList, value); }
        }

        #endregion

        #region SelectedColorsList

        private SourceList<int> _SelectedColorsList = new SourceList<int>();

        /// <summary>
        ///     Список выделенных цветов в окне
        /// </summary>
        public SourceList<int> SelectedColorsList
        {
            get { return _SelectedColorsList; }
            set { this.RaiseAndSetIfChanged(ref _SelectedColorsList, value); }
        }

        #endregion

        #region FilterEntryType

        #region FilterEntryTypeIsChecked

        private bool _FilterEntryTypeIsChecked = false;

        public bool FilterEntryTypeIsChecked
        {
            get { return _FilterEntryTypeIsChecked; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypeIsChecked, value); }
        }

        #endregion

        #region FilterEntryTypeSystemStateIsChecked

        private bool _FilterEntryTypeSystemStateIsChecked = false;

        public bool FilterEntryTypeSystemStateIsChecked
        {
            get { return _FilterEntryTypeSystemStateIsChecked; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypeSystemStateIsChecked, value); }
        }

        #endregion

        #region FilterEntryTypeLeakageDetectionIsChecked

        private bool _FilterEntryTypeLeakageDetectionIsChecked = false;

        public bool FilterEntryTypeLeakageDetectionIsChecked
        {
            get { return _FilterEntryTypeLeakageDetectionIsChecked; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypeLeakageDetectionIsChecked, value); }
        }

        #endregion

        #region FilterEntryTypeHeadPressureControlIsChecked

        private bool _FilterEntryTypeHeadPressureControlIsChecked = false;

        public bool FilterEntryTypeHeadPressureControlIsChecked
        {
            get { return _FilterEntryTypeHeadPressureControlIsChecked; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypeHeadPressureControlIsChecked, value); }
        }

        #endregion

        #region FilterEntryTypePIGControlIsChecked

        private bool _FilterEntryTypePIGControlIsChecked = false;

        public bool FilterEntryTypePIGControlIsChecked
        {
            get { return _FilterEntryTypePIGControlIsChecked; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypePIGControlIsChecked, value); }
        }

        #endregion

        #region FilterEntryTypeSystemMessagesIsChecked

        private bool _FilterEntryTypeSystemMessagesIsChecked = false;

        public bool FilterEntryTypeSystemMessagesIsChecked
        {
            get { return _FilterEntryTypeSystemMessagesIsChecked; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypeSystemMessagesIsChecked, value); }
        }

        #endregion

        #region FilterEntryTypeUserActionsIsChecked

        private bool _FilterEntryTypeUserActionsIsChecked = false;

        public bool FilterEntryTypeUserActionsIsChecked
        {
            get { return _FilterEntryTypeUserActionsIsChecked; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypeUserActionsIsChecked, value); }
        }

        #endregion

        #region FilterEntryTypeHeadPressureControlIsVisible

        private bool _FilterEntryTypeHeadPressureControlIsVisible = true;

        public bool FilterEntryTypeHeadPressureControlIsVisible
        {
            get { return _FilterEntryTypeHeadPressureControlIsVisible; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypeHeadPressureControlIsVisible, value); }
        }

        #endregion

        #region FilterEntryTypePIGControlIsVisible

        private bool _FilterEntryTypePIGControlIsVisible = true;

        public bool FilterEntryTypePIGControlIsVisible
        {
            get { return _FilterEntryTypePIGControlIsVisible; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypePIGControlIsVisible, value); }
        }

        #endregion

        #region FilterEntryTypeSystemMessagesIsVisible

        private bool _FilterEntryTypeSystemMessagesIsVisible = true;

        public bool FilterEntryTypeSystemMessagesIsVisible
        {
            get { return _FilterEntryTypeSystemMessagesIsVisible; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypeSystemMessagesIsVisible, value); }
        }

        #endregion

        #region FilterEntryTypeUserActionsIsVisible

        private bool _FilterEntryTypeUserActionsIsVisible = true;

        public bool FilterEntryTypeUserActionsIsVisible
        {
            get { return _FilterEntryTypeUserActionsIsVisible; }
            set { this.RaiseAndSetIfChanged(ref _FilterEntryTypeUserActionsIsVisible, value); }
        }

        #endregion

        #endregion

        #region FilterByObjects

        public ListCollectionView FilterObjectsListView { get; private set; }

        #region FilterObjectsListIsLoaded

        private bool _FilterByObjectsListIsLoading = false;

        public bool FilterByObjectsListIsLoading
        {
            get { return _FilterByObjectsListIsLoading; }
            set { this.RaiseAndSetIfChanged(ref _FilterByObjectsListIsLoading, value); }
        }

        #endregion

        #region FilterByObjectsList

        private RadObservableCollectionEx<CategoryViewModel> _FilterObjectsList;

        public RadObservableCollectionEx<CategoryViewModel> FilterByObjectsList
        {
            get { return _FilterObjectsList ?? (_FilterObjectsList = new RadObservableCollectionEx<CategoryViewModel>()); }
            set
            {
                _FilterObjectsList = value;
                this.RaisePropertyChanged(nameof(FilterByObjectsList));
            }
        }

        #endregion

        #region FilterByColorsList

        private SourceList<JournalColorGroupViewModel> _FilterByColorsList = null;

        public SourceList<JournalColorGroupViewModel> FilterByColorsList
        {
            get { return _FilterByColorsList; }
            set { this.RaiseAndSetIfChanged(ref _FilterByColorsList, value); }
        }

        #endregion

        #region AppliedFilterByColorsList

        private SourceList<JournalColorGroupViewModel> _AppliedFilterByColorsList = null;

        public SourceList<JournalColorGroupViewModel> AppliedFilterByColorsList
        {
            get { return _AppliedFilterByColorsList; }
            set { this.RaiseAndSetIfChanged(ref _AppliedFilterByColorsList, value); }
        }

        #endregion

        #region FilterByObjectsSelectedString

        private string _FilterByObjectsSelectedString = string.Empty;

        /// <summary>
        ///     Фильтр по объектам - текстовый фильтр - строка
        /// </summary>
        public string FilterByObjectsSelectedString
        {
            get { return _FilterByObjectsSelectedString; }
            set { this.RaiseAndSetIfChanged(ref _FilterByObjectsSelectedString, value); }
        }

        #endregion

        #region FilterByObjectsIsEnabled

        private bool _FilterByObjectsIsEnabled = false;

        public bool FilterByObjectsIsEnabled
        {
            get { return _FilterByObjectsIsEnabled; }
            set { this.RaiseAndSetIfChanged(ref _FilterByObjectsIsEnabled, value); }
        }

        #endregion

        #region FilterByColorsIsEnabled

        private bool _FilterByColorsIsEnabled = false;

        public bool FilterByColorsIsEnabled
        {
            get { return _FilterByColorsIsEnabled; }
            set { this.RaiseAndSetIfChanged(ref _FilterByColorsIsEnabled, value); }
        }

        #endregion

        #region FilterObjectsIsChecked

        private bool _FilterObjectsIsChecked = false;

        public bool FilterObjectsIsChecked
        {
            get { return _FilterObjectsIsChecked; }
            set { this.RaiseAndSetIfChanged(ref _FilterObjectsIsChecked, value); }
        }

        #endregion

        #region FilterColorsIsChecked

        private bool _FilterColorsIsChecked = false;

        public bool FilterColorsIsChecked
        {
            get { return _FilterColorsIsChecked; }
            set { this.RaiseAndSetIfChanged(ref _FilterColorsIsChecked, value); }
        }

        #endregion

        #region ApplyFilterByObjectsCommandIsEnabled

        private bool _ApplyFilterByObjectsCommandIsEnabled = false;

        public bool ApplyFilterByObjectsCommandIsEnabled
        {
            get { return _ApplyFilterByObjectsCommandIsEnabled; }
            set { this.RaiseAndSetIfChanged(ref _ApplyFilterByObjectsCommandIsEnabled, value); }
        }

        #endregion

        #region ApplyFilterByColorsCommandIsEnabled

        private bool _ApplyFilterByColorsCommandIsEnabled = false;

        public bool ApplyFilterByColorsCommandIsEnabled
        {
            get { return _ApplyFilterByColorsCommandIsEnabled; }
            set { this.RaiseAndSetIfChanged(ref _ApplyFilterByColorsCommandIsEnabled, value); }
        }

        #endregion

        #region ResetFilterObjectsCommandIsEnabled

        private bool _ResetFilterObjectsCommandIsEnabled = false;

        public bool ResetFilterObjectsCommandIsEnabled
        {
            get { return _ResetFilterObjectsCommandIsEnabled; }
            set { this.RaiseAndSetIfChanged(ref _ResetFilterObjectsCommandIsEnabled, value); }
        }

        #endregion

        #region ResetFilterColorsCommandIsEnabled

        private bool _ResetFilterColorsCommandIsEnabled = false;

        public bool ResetFilterColorsCommandIsEnabled
        {
            get { return _ResetFilterColorsCommandIsEnabled; }
            set { this.RaiseAndSetIfChanged(ref _ResetFilterColorsCommandIsEnabled, value); }
        }

        #endregion

        #region ObjectsListIsDropDownOpen

        private bool _ObjectsListIsDropDownOpen = false;

        /// <summary>
        ///     Признак видимости окна фильтрации по объектам/тексту сообщения
        /// </summary>
        public bool ObjectsListIsDropDownOpen
        {
            get { return _ObjectsListIsDropDownOpen; }
            set { this.RaiseAndSetIfChanged(ref _ObjectsListIsDropDownOpen, value); }
        }

        #endregion

        #region ColorsListIsDropDownOpen

        private bool _ColorsListIsDropDownOpen = false;

        /// <summary>
        ///     Признак видимости окна фильтрации по цвету сообщения
        /// </summary>
        public bool ColorsListIsDropDownOpen
        {
            get { return _ColorsListIsDropDownOpen; }
            set { this.RaiseAndSetIfChanged(ref _ColorsListIsDropDownOpen, value); }
        }

        #endregion

        #endregion

        #region FilterByMessageText

        private string _FilterByMessageText = string.Empty;

        /// <summary>
        ///     Фильтр по тексту сообщения
        /// </summary>
        public string FilterByMessageText
        {
            get { return _FilterByMessageText; }
            set { this.RaiseAndSetIfChanged(ref _FilterByMessageText, value); }
        }

        #endregion

        #region JournalMode

        private DataSourceMode _JournalMode = DataSourceMode.RealTime;

        public DataSourceMode JournalMode
        {
            get { return _JournalMode; }
            set { this.RaiseAndSetIfChanged(ref _JournalMode, value); }
        }

        #endregion

        #region DateTime

        #region FilterDateTimeIsEnabled

        private bool _FilterDateTimeIsEnabled = false;

        public bool FilterDateTimeIsEnabled
        {
            get { return _FilterDateTimeIsEnabled; }
            set { this.RaiseAndSetIfChanged(ref _FilterDateTimeIsEnabled, value); }
        }

        #endregion

        #region DateTimePickerStart

        private DateTime? _DateTimePickerStart;

        public DateTime DateTimePickerStart
        {
            get { return (DateTime)(_DateTimePickerStart ?? (_DateTimePickerStart = DateTime.Now.AddHours(-24))); }
            set
            {
                this.RaiseAndSetIfChanged(
                    ref _DateTimePickerStart,
                    new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, 0));
                this.RaisePropertyChanged(nameof(DateTimePickerEnd));
            }
        }

        #endregion

        #region DateTimePickerEnd

        private DateTime? _DateTimePickerEnd;

        public DateTime DateTimePickerEnd
        {
            get { return (DateTime)(_DateTimePickerEnd ?? (_DateTimePickerEnd = DateTime.Now)); }
            set
            {
                this.RaiseAndSetIfChanged(
                    ref _DateTimePickerEnd,
                    new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, 0, 0));
                this.RaisePropertyChanged(nameof(DateTimePickerStart));
            }
        }

        #endregion

        #endregion

        #region FiltersIsChanged

        private bool _FiltersIsChanged = false;

        /// <summary>
        ///     Признак изменения фильтров
        /// </summary>
        public bool FiltersIsChanged
        {
            get { return _FiltersIsChanged; }
            set { this.RaiseAndSetIfChanged(ref _FiltersIsChanged, value); }
        }

        #endregion

        #region ApplyFilterIsProcessing

        private bool _ApplyFilterIsProcessing = false;

        /// <summary>
        ///     Выполняется применение фильтра
        /// </summary>
        public bool ApplyFilterIsProcessing
        {
            get { return _ApplyFilterIsProcessing; }
            set { this.RaiseAndSetIfChanged(ref _ApplyFilterIsProcessing, value); }
        }

        #endregion

        #region ExportIsChecked

        private bool _ExportIsChecked = false;

        public bool ExportIsChecked
        {
            get { return _ExportIsChecked; }
            set { this.RaiseAndSetIfChanged(ref _ExportIsChecked, value); }
        }

        #endregion

        #region ExportIsEnabled

        private bool _ExportIsEnabled = false;

        public bool ExportIsEnabled
        {
            get { return _ExportIsEnabled; }
            set { this.RaiseAndSetIfChanged(ref _ExportIsEnabled, value); }
        }

        #endregion

        #region IsExportButtonVisible

        private ObservableAsPropertyHelper<bool> _isExportButtonVisible;

        public bool IsExportButtonVisible
        {
            get { return _isExportButtonVisible.Value; }
        }

        private IDisposable InitializeIsExportButtonVisibleProperty()
        {
            return _isExportButtonVisible = this.ObservableForProperty(x => x.CurrentACLs)
                                                .Select(_ => GetIsExportButtonVisible())
                                                .ToProperty(this, x => x.IsExportButtonVisible, GetIsExportButtonVisible());
        }

        private bool GetIsExportButtonVisible()
        {
            return CurrentACLs != null && CurrentACLs.Contains(ACL.CAN_EXPORT_JOURNAL_ENTRIES);
        }

        #endregion

        #region ExportProgress

        private int _ExportProgress = 0;

        public int ExportProgress
        {
            get { return _ExportProgress; }
            set { this.RaiseAndSetIfChanged(ref _ExportProgress, value); }
        }

        #endregion

        #region ExportProgressMessage

        private string _ExportProgressMessage = string.Empty;

        public string ExportProgressMessage
        {
            get { return _ExportProgressMessage; }
            set { this.RaiseAndSetIfChanged(ref _ExportProgressMessage, value); }
        }

        #endregion

        #region ExportManagerIsExecuted

        private bool _ExportManagerIsExecuted = false;

        public bool ExportManagerIsExecuted
        {
            get { return _ExportManagerIsExecuted; }
            set { this.RaiseAndSetIfChanged(ref _ExportManagerIsExecuted, value); }
        }

        #endregion

        #region FilterColorsListButtonIsEnabled

        private bool _FilterColorsListButtonIsEnabled = false;

        public bool FilterColorsListButtonIsEnabled
        {
            get { return _FilterColorsListButtonIsEnabled; }
            set { this.RaiseAndSetIfChanged(ref _FilterColorsListButtonIsEnabled, value); }
        }

        #endregion

        #endregion

        #region Implementation of IDataErrorInfo

        string IDataErrorInfo.Error
        {
            get { return null; }
        }

        string IDataErrorInfo.this[string propertyName]
        {
            get { return GetValidationError(propertyName); }
        }

        #endregion

        #region Validation

        private static readonly string[] ValidatedProperties =
        {
            "DateTimePickerStart",
            "DateTimePickerEnd"
        };

        public bool ViewModelFiltersIsValid
        {
            get { return ValidatedProperties.All(property => GetValidationError(property) == null); }
        }

        private string GetValidationError(string propertyName)
        {
            if (Array.IndexOf(ValidatedProperties, propertyName) < 0)
                return null;

            string error = null;

            switch (propertyName)
            {
                case "DateTimePickerStart":
                case "DateTimePickerEnd":
                    error = ValidateDateTimes();

                    break;

                default:
                    Debug.Fail(string.Format("Unexpected property being validated on {0}: {1}", GetType().Name,
                                             propertyName));

                    break;
            }

            return error;
        }

        private string ValidateDateTimes()
        {
            if (_DateTimePickerStart > _DateTimePickerEnd)
                return Strings.Error_StartDateMoreThanEndDate;

            if (!_DateTimePickerStart.HasValue || !_DateTimePickerEnd.HasValue)
                return null;

            return _DateTimePickerStart.Value.RoundDown(DateTimeExtensions.RoundTo.Minute)
                == _DateTimePickerEnd.Value.RoundDown(DateTimeExtensions.RoundTo.Minute)
                ? Strings.Error_StartDateEqualsEndDate
                : null;
        }

        #endregion Validation

        #region Implementation of IJournalViewModel

        #region AllEntries

        private AsyncVirtualizingCollectionWithItemComparer<JournalEntryViewModel> _AllEntries;

        public AsyncVirtualizingCollectionWithItemComparer<JournalEntryViewModel> AllEntries
        {
            get { return _AllEntries; }
            set
            {
                _AllEntries = value;
                this.RaisePropertyChanged(nameof(AllEntries));
            }
        }

        #endregion

        #endregion

        #region Implementation of IViewModel

        public void Initialize()
        {
            switch (Settings.Version)
            {
                case ApplicationVersion.ManualLDS:
                case ApplicationVersion.LDS:
                    FilterEntryTypeHeadPressureControlIsVisible = false;
                    FilterEntryTypePIGControlIsVisible = false;

                    break;
            }

            //EventAggregator.GetEvent<RHDAActivatedEvent>().Subscribe(RHDAIsActivated, ThreadOption.BackgroundThread);
            //EventAggregator.GetEvent<RHDAFailedEvent>().Subscribe(RHDAFailed, ThreadOption.BackgroundThread);

            _FilterObjectsLoadingWorker.DoWork += FilterObjectsLoadingWorkerOnDoWork;
            _FilterObjectsLoadingWorker.RunWorkerCompleted += FilterObjectsLoadingWorkerOnRunWorkerCompleted;

            _loadEntriesFromArchiveWorker.DoWork += LoadEntriesFromArchiveWorkerOnDoWork;
            _loadEntriesFromArchiveWorker.RunWorkerCompleted += LoadEntriesFromArchiveWorkerOnRunWorkerCompleted;

            _ExportEntriesWorker.DoWork += ExportEntriesWorkerOnDoWork;
            _ExportEntriesWorker.RunWorkerCompleted += ExportEntriesWorkerOnRunWorkerCompleted;
            _ExportEntriesWorker.ProgressChanged += ExportEntriesWorkerOnProgressChanged;
            _ExportEntriesWorker.WorkerReportsProgress = true;
            _ExportEntriesWorker.WorkerSupportsCancellation = true;

            FilterByColorsList = new SourceList<JournalColorGroupViewModel>();
            AppliedFilterByColorsList = new SourceList<JournalColorGroupViewModel>();

            SubscribeToPropertiesChanged();
            InitializeIsExportButtonVisibleProperty();
        }

        #endregion

        /*#region Implementation of IRHDAAvailableAware

        #region RHDAIsActive

        private bool _RHDAIsActive = false;

        public bool RHDAIsActive
        {
            get { return _RHDAIsActive; }
            set { this.RaiseAndSetIfChanged(ref _RHDAIsActive, value); }
        }

        #endregion

        public void RHDAIsActivated(object obj)
        {
            SetAndWaitingNewLayerProcessing();

            RHDAIsActive = true;

            ResetNewLayerProcessing();
        }

        public void RHDAFailed(object obj)
        {
            SetAndWaitingNewLayerProcessing();

            RHDAIsActive = false;

            _SchedulerProvider.MainThread.Schedule(() =>
            {
                BusyIndicatorMessage = Common.Properties.Resources.RHDANotAvailable;
                BusyIndicatorState = true;

                ResetNewLayerProcessing();
            });
        }

        #endregion*/

        #region ChangeDateTimeRangeRequest

        private readonly InteractionRequest<Confirmation> _ChangeDateTimeRangeRequest = new InteractionRequest<Confirmation>();

        private readonly object _allEntriesLocker = new object();

        public IInteractionRequest ChangeDateTimeRangeRequest
        {
            get { return _ChangeDateTimeRangeRequest; }
        }

        #endregion

        #region ExportFailedRequest

        private InteractionRequest<Notification> _ExportFailedRequest;

        public InteractionRequest<Notification> ExportFailedRequest
        {
            get { return _ExportFailedRequest ?? (_ExportFailedRequest = new InteractionRequest<Notification>()); }
        }

        #endregion

        #region Implementation of IDisposable

        #region Disposable

        private CompositeDisposable _disposable;

        private CompositeDisposable Disposable
        {
            get { return _disposable ?? (_disposable = new CompositeDisposable()); }
        }

        #endregion

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disposable.Dispose();
            }

            base.Dispose(disposing);
        }

        #endregion

        public override void OnNavigatedTo(NavigationContext navigationContext)
        {
            EventAggregator.GetEvent<HelpSectionChangeEvent>().Publish(_keyword);
        }
    }
}
