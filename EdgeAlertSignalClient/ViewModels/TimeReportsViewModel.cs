using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EdgeAlertSignalClient.Handlers;
using EdgeAlertSignalClient.Models;
using EdgeAlertSignalClient.Services;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Collections.Generic;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Message;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using SkiaSharp;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using Axis = LiveChartsCore.SkiaSharpView.Axis;
using ClosedXML.Excel;
using System.IO; // Explicit alias

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class TimeReportsViewModel : ObservableObject, IRecipient<MainViewModelInitialisedMessage>
    {
        private readonly ILogger<TimeReportsViewModel> _log;
        private readonly JSONHandler _jsonHandler;
        private readonly MainViewModel _mainViewModel; // Keep if needed for other logic, else remove
        private readonly IEdgeAlertService _edgeAlertService;

        private bool _isViewInitialized = false;
        private readonly object _initLock = new object();

        // --- Filter Properties ---
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadReportDataCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
        private DateTime _startDate;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(LoadReportDataCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
        private DateTime _endDate;

        [ObservableProperty]
        private ObservableCollection<string> _odsPractices;

        [ObservableProperty]
        private ObservableCollection<string> _userNames;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAutoLoadReport))] // Trigger CanAutoLoadReport check
        [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
        private string _selectedOdsPractice;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAutoLoadReport))] // Trigger CanAutoLoadReport check
        [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
        private string _selectedUserName;

        // --- Data Collections ---
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
        private ObservableCollection<SummarizedReportData> _summaryReportData;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
        private ObservableCollection<DetailedReportData> _detailedReportData;

        // --- Status Properties ---
        [ObservableProperty]
        private string _statusMessage;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanAutoLoadReport))]
        [NotifyCanExecuteChangedFor(nameof(LoadReportDataCommand))]
        [NotifyCanExecuteChangedFor(nameof(ExportReportCommand))]
        private bool _isBusy;

        // --- Sorting ---
        private string _lastDetailedSortColumn = "StateStartTime";
        private ListSortDirection _lastDetailedSortDirection = ListSortDirection.Descending;

        // --- >> NEW: Summary View Sorting Fields << ---
        private string _lastSummarySortColumn = "Date"; // Default sort column for summary
        private ListSortDirection _lastSummarySortDirection = ListSortDirection.Descending; // Default sort direction

        // --- LiveCharts Properties (Pie Chart, Heatmap, Step Chart) ---
        [ObservableProperty]
        private bool _showOfflineData = false;

        [ObservableProperty]
        private ISeries[] _statePieChartSeries;

        [ObservableProperty]
        private TimeSpan _totalActiveDuration;
        [ObservableProperty]
        private double _totalActivePercentage;
        [ObservableProperty]
        private TimeSpan _totalAwayDuration;
        [ObservableProperty]
        private double _totalAwayPercentage;
        [ObservableProperty]
        private TimeSpan _totalOfflineDuration;
        [ObservableProperty]
        private double _totalOfflinePercentage;

        // --- Summary Card Properties ---
        [ObservableProperty]
        private string _totalUsersPresentSummary = "0";
        [ObservableProperty]
        private string _totalWorkHoursSummary = "0h 0m";
        [ObservableProperty]
        private string _totalAwayTimeSummary = "0h 0m";
        [ObservableProperty]
        private string _totalOfflineTimeSummary = "0h 0m";

        // --- Presence Heatmap Properties ---
        [ObservableProperty]
        private ISeries[] _presenceHeatMapSeries = Array.Empty<ISeries>();
        [ObservableProperty]
        private Axis[] _presenceXAxes = { new Axis() };
        [ObservableProperty]
        private Axis[] _presenceYAxes = { new Axis() };
        private List<DateTime> _presenceDateMap = new List<DateTime>();
        private List<string> _presenceUserMap = new List<string>();

        // --- Drill-Down Step Chart State & Data Properties ---
        [ObservableProperty]
        private bool _isDailyActivityVisible = false;
        [ObservableProperty]
        private string _selectedActivityUser;
        [ObservableProperty]
        private DateTime? _selectedActivityDate;
        [ObservableProperty]
        private ISeries[] _dailyActivitySeries = Array.Empty<ISeries>();
        [ObservableProperty]
        private Axis[] _dailyActivityXAxis = { new Axis() };
        [ObservableProperty]
        private Axis[] _dailyActivityYAxis = { new Axis() };

        // --- Flexible Timeframe Properties ---
        [ObservableProperty]
        private ObservableCollection<string> _dateRangeOptions;
        [ObservableProperty]
        private string _selectedDateRange;


        // --- Constructor ---
        public TimeReportsViewModel(
                    ILogger<TimeReportsViewModel> log,
                    JSONHandler jsonHandler,
                    MainViewModel mainViewModel,
                    IEdgeAlertService edgeAlertService)
        {
            _log = log;
            _jsonHandler = jsonHandler;
            _mainViewModel = mainViewModel;
            _edgeAlertService = edgeAlertService;

            _log.LogInformation("TimeReportsViewModel initializing...");

            WeakReferenceMessenger.Default.Register<MainViewModelInitialisedMessage>(this);

            // Initialize collections FIRST
            OdsPractices = new ObservableCollection<string>();
            UserNames = new ObservableCollection<string>();
            SummaryReportData = new ObservableCollection<SummarizedReportData>();
            DetailedReportData = new ObservableCollection<DetailedReportData>();
            DateRangeOptions = new ObservableCollection<string>
            {
                "Custom Range", "Today", "Yesterday", "Last 7 Days", "Last 30 Days", "This Month", "Last Month"
            };

            // Initialize chart properties
            StatePieChartSeries = Array.Empty<ISeries>();
            PresenceHeatMapSeries = Array.Empty<ISeries>();
            PresenceXAxes = new Axis[] { new Axis() };
            PresenceYAxes = new Axis[] { new Axis() };
            DailyActivitySeries = Array.Empty<ISeries>();
            DailyActivityXAxis = new Axis[] { new Axis() };
            DailyActivityYAxis = new Axis[] { new Axis() };

            // Set default date range
            StartDate = DateTime.Today;
            EndDate = DateTime.Today;
            SelectedDateRange = GetDateRangeNameFromDates(StartDate, EndDate);

            _log.LogInformation("TimeReportsViewModel initialized.");
        }

        // --- Property Changed Handlers ---

        partial void OnShowOfflineDataChanged(bool value)
        {
            _log.LogInformation($"Show offline data toggled to: {value}");
            // Re-process charts that depend on this flag if data exists
            if (SummaryReportData != null && SummaryReportData.Any())
            {
                ProcessSummaryDataForPieChart(SummaryReportData); // Pie chart depends on this
                // If the drill-down step chart was visible, re-process it
                if (IsDailyActivityVisible && SelectedActivityUser != null && SelectedActivityDate != null)
                {
                    ProcessDailyActivityStepData(SelectedActivityUser, SelectedActivityDate.Value);
                }
            }
        }

        async partial void OnStartDateChanged(DateTime value)
        {
            _log.LogDebug($"OnStartDateChanged triggered with value: {value:yyyy-MM-dd}");
            if (DateRangeOptions == null) { _log.LogWarning("DateRangeOptions not ready."); return; }
            if (value > EndDate) { EndDate = value; } else { UpdateSelectedDateRangeBasedOnDates(); }
            // Don't auto-load here, Load button handles custom range
        }

        async partial void OnEndDateChanged(DateTime value)
        {
            _log.LogDebug($"OnEndDateChanged triggered with value: {value:yyyy-MM-dd}");
            if (DateRangeOptions == null) { _log.LogWarning("DateRangeOptions not ready."); return; }
            if (value < StartDate) { StartDate = value; } else { UpdateSelectedDateRangeBasedOnDates(); }
            // Don't auto-load here, Load button handles custom range
        }

        async partial void OnSelectedDateRangeChanged(string value)
        {
            _log.LogDebug($"OnSelectedDateRangeChanged triggered with value: '{value}'");
            if (DateRangeOptions == null || DateRangeOptions.Count == 0 || string.IsNullOrEmpty(value) || value == "Custom Range")
            {
                return; // Ignore null, empty, or "Custom Range" selections here
            }
            string currentRangeFromDates = GetDateRangeNameFromDates(StartDate, EndDate);
            if (value != currentRangeFromDates)
            {
                _log.LogInformation($"ComboBox selection '{value}' chosen. Applying preset range via command.");
                if (SetDateRangeCommand.CanExecute(value))
                {
                    // SetDateRangeCommand now triggers LoadReportData internally
                    await SetDateRangeCommand.ExecuteAsync(value);
                }
            }
            else { _log.LogDebug($"ComboBox selection '{value}' matches current dates. No action needed."); }
        }

        // --- NEW: Auto-load trigger for ODS Practice change ---
        async partial void OnSelectedOdsPracticeChanged(string value)
        {
            if (string.IsNullOrEmpty(value)) return; // Ignore if cleared during init

            IsBusy = true; // Set busy while loading users
            StatusMessage = $"Loading users for {value}...";
            UserNames.Clear();
            UserNames.Add("All");
            bool usersLoaded = false;

            try
            {
                if (value == "All")
                {
                    _log.LogInformation("ODS Selection 'All': User filter reset to 'All'.");
                    usersLoaded = true; // Mark as successful for auto-load trigger
                }
                else
                {
                    var location = _jsonHandler.GetLocationByPractice(value);
                    if (location == null || string.IsNullOrEmpty(location.ODS))
                    {
                        _log.LogError($"Could not find ODS code for selected practice: {value}");
                        StatusMessage = $"Error finding ODS code for {value}.";
                    }
                    else
                    {
                        string targetOdsCode = location.ODS;
                        var permittedUsers = await _edgeAlertService.GetPermittedUsersForOdsAsync(targetOdsCode);
                        if (permittedUsers != null)
                        {
                            foreach (var userName in permittedUsers.OrderBy(un => un)) { UserNames.Add(userName); }
                            _log.LogInformation($"Loaded {permittedUsers.Count} users for ODS {targetOdsCode} ({value}).");
                            StatusMessage = $"Loaded users for {value}.";
                            usersLoaded = true; // Mark as successful
                        }
                        else
                        {
                            _log.LogWarning($"Failed to load users for ODS {targetOdsCode} ({value}). Service returned null.");
                            StatusMessage = $"Failed to load users for {value}.";
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error loading users for selected ODS practice: {value}");
                StatusMessage = $"Error loading users for {value}.";
            }
            finally
            {
                SelectedUserName = "All"; // Reset user selection
                IsBusy = false;
                if (usersLoaded)
                {
                    // Auto-load report data *after* user list is populated/reset
                    await TriggerAutoLoad("ODS Practice Change");
                }
                else if (!StatusMessage.StartsWith("Error") && !StatusMessage.StartsWith("Failed"))
                {
                    StatusMessage = "Select criteria and load report.";
                }
            }
        }

        // --- NEW: Auto-load trigger for User Name change ---
        async partial void OnSelectedUserNameChanged(string value)
        {
            if (string.IsNullOrEmpty(value)) return; // Ignore if cleared during init etc.
            // Auto-load report data when user selection changes
            await TriggerAutoLoad("User Name Change");
        }

        // --- Helper methods for Date Range and Auto-Load ---
        private bool CanAutoLoadReport => !IsBusy && !string.IsNullOrEmpty(SelectedOdsPractice) && !string.IsNullOrEmpty(SelectedUserName);

        private async Task TriggerAutoLoad(string triggerSource)
        {
            _log.LogInformation($"Auto-load triggered by: {triggerSource}. Checking conditions...");
            if (CanAutoLoadReport && LoadReportDataCommand.CanExecute(null))
            {
                _log.LogInformation("Conditions met. Executing LoadReportDataCommand.");
                await LoadReportDataCommand.ExecuteAsync(null);
            }
            else
            {
                string reason = IsBusy ? "ViewModel is busy." : "Filters not ready or Load command cannot execute.";
                _log.LogInformation($"Auto-load skipped. Reason: {reason}");
            }
        }

        private void UpdateSelectedDateRangeBasedOnDates()
        {
            if (DateRangeOptions == null) return;
            string rangeName = GetDateRangeNameFromDates(StartDate, EndDate);
            SetProperty(ref _selectedDateRange, rangeName, nameof(SelectedDateRange));
            _log.LogDebug($"Updated SelectedDateRange to '{rangeName}' based on DatePickers.");
        }

        private string GetDateRangeNameFromDates(DateTime start, DateTime end)
        {
            DateTime now = DateTime.Today;
            DateTime startDateOnly = start.Date;
            DateTime endDateOnly = end.Date;

            if (startDateOnly == now && endDateOnly == now) return "Today";
            DateTime yesterday = now.AddDays(-1);
            if (startDateOnly == yesterday && endDateOnly == yesterday) return "Yesterday";
            if (startDateOnly == now.AddDays(-6) && endDateOnly == now) return "Last 7 Days";
            if (startDateOnly == now.AddDays(-29) && endDateOnly == now) return "Last 30 Days";
            DateTime firstDayThisMonth = new DateTime(now.Year, now.Month, 1);
            if (startDateOnly == firstDayThisMonth && endDateOnly == now) return "This Month";
            DateTime firstDayLastMonth = firstDayThisMonth.AddMonths(-1);
            DateTime lastDayLastMonth = firstDayThisMonth.AddDays(-1);
            if (startDateOnly == firstDayLastMonth && endDateOnly == lastDayLastMonth) return "Last Month";

            return "Custom Range";
        }

        // --- Data Processing Methods ---

        private void UpdateSummaryCards(IEnumerable<SummarizedReportData> summaryData)
        {
            if (summaryData == null || !summaryData.Any())
            {
                _log.LogWarning("No summary data provided to UpdateSummaryCards. Resetting card values.");
                TotalUsersPresentSummary = "0"; TotalWorkHoursSummary = "0h 0m"; TotalAwayTimeSummary = "0h 0m"; TotalOfflineTimeSummary = "0h 0m"; return;
            }
            try
            {
                _log.LogInformation("Calculating summary statistics for data cards...");
                int distinctUsers = summaryData.Select(s => s.UserID).Distinct().Count();
                TotalUsersPresentSummary = distinctUsers.ToString();
                double totalActiveSeconds = summaryData.Sum(s => s.TotalActiveSeconds);
                double totalAwaySeconds = summaryData.Sum(s => s.TotalAwaySeconds);
                double totalOfflineSeconds = summaryData.Sum(s => s.TotalOfflineSeconds);
                TotalWorkHoursSummary = FormatTimeSpanFromSeconds(totalActiveSeconds); // Using Active time as Work Hours
                TotalAwayTimeSummary = FormatTimeSpanFromSeconds(totalAwaySeconds);
                TotalOfflineTimeSummary = FormatTimeSpanFromSeconds(totalOfflineSeconds);
                _log.LogInformation($"Summary Card Stats: Users={TotalUsersPresentSummary}, Work={TotalWorkHoursSummary}, Away={TotalAwayTimeSummary}, Offline={TotalOfflineTimeSummary}");
            }
            catch (Exception ex) { _log.LogError(ex, "Error calculating summary card statistics."); TotalUsersPresentSummary = "Error"; TotalWorkHoursSummary = "Error"; TotalAwayTimeSummary = "Error"; TotalOfflineTimeSummary = "Error"; }
        }

        private string FormatTimeSpanFromSeconds(double totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            TimeSpan timeSpan = TimeSpan.FromSeconds(totalSeconds);
            return $"{(int)timeSpan.TotalHours}h {timeSpan.Minutes}m {timeSpan.Seconds}s";
        }

        private void ProcessSummaryDataForPieChart(ObservableCollection<SummarizedReportData> summaryData)
        {
            _log.LogInformation("Processing summary data for pie chart...");
            TotalActiveDuration = TimeSpan.Zero; TotalActivePercentage = 0; TotalAwayDuration = TimeSpan.Zero; TotalAwayPercentage = 0; TotalOfflineDuration = TimeSpan.Zero; TotalOfflinePercentage = 0;
            if (summaryData == null || !summaryData.Any()) { _log.LogWarning("No summary data available for pie chart. Clearing."); StatePieChartSeries = Array.Empty<ISeries>(); OnPropertyChanged(nameof(StatePieChartSeries)); return; }

            try
            {
                double totalActiveSeconds = summaryData.Sum(s => s.TotalActiveSeconds);
                double totalAwaySeconds = summaryData.Sum(s => s.TotalAwaySeconds);
                // **** REVERTED CHANGE: Only include offline seconds if ShowOfflineData is true ****
                double totalOfflineSeconds = ShowOfflineData ? summaryData.Sum(s => s.TotalOfflineSeconds) : 0;

                // Adjust total based on whether offline is included
                double totalSeconds = totalActiveSeconds + totalAwaySeconds + (ShowOfflineData ? totalOfflineSeconds : 0);
                // **** END REVERTED CHANGE ****

                if (totalSeconds <= 0) { _log.LogWarning("Total time across relevant states is zero. Clearing pie chart."); StatePieChartSeries = Array.Empty<ISeries>(); }
                else
                {
                    double activePercentage = totalActiveSeconds / totalSeconds;
                    double awayPercentage = totalAwaySeconds / totalSeconds;
                    // **** REVERTED CHANGE: Calculate offline percentage only if shown ****
                    double offlinePercentage = ShowOfflineData ? totalOfflineSeconds / totalSeconds : 0;

                    TotalActiveDuration = TimeSpan.FromSeconds(totalActiveSeconds); TotalActivePercentage = activePercentage;
                    TotalAwayDuration = TimeSpan.FromSeconds(totalAwaySeconds); TotalAwayPercentage = awayPercentage;
                    TotalOfflineDuration = TimeSpan.FromSeconds(totalOfflineSeconds); TotalOfflinePercentage = offlinePercentage;

                    var activeColor = new SKColor(76, 175, 80); var awayColor = new SKColor(255, 152, 0); var offlineColor = new SKColor(158, 158, 158);
                    var pieSeries = new List<ISeries>();

                    if (totalActiveSeconds > 0) pieSeries.Add(new LiveChartsCore.SkiaSharpView.PieSeries<double> { Name = "Active", Values = new double[] { totalActiveSeconds }, Fill = new SolidColorPaint(activeColor), /* ... rest ... */ ToolTipLabelFormatter = point => $"Active: {TimeSpan.FromSeconds(totalActiveSeconds):hh\\:mm\\:ss} ({activePercentage:P1})" });
                    if (totalAwaySeconds > 0) pieSeries.Add(new LiveChartsCore.SkiaSharpView.PieSeries<double> { Name = "Away", Values = new double[] { totalAwaySeconds }, Fill = new SolidColorPaint(awayColor), /* ... rest ... */ ToolTipLabelFormatter = point => $"Away: {TimeSpan.FromSeconds(totalAwaySeconds):hh\\:mm\\:ss} ({awayPercentage:P1})" });
                    // **** REVERTED CHANGE: Only add Offline slice if ShowOfflineData is true AND > 0 ****
                    if (ShowOfflineData && totalOfflineSeconds > 0) pieSeries.Add(new LiveChartsCore.SkiaSharpView.PieSeries<double> { Name = "Offline", Values = new double[] { totalOfflineSeconds }, Fill = new SolidColorPaint(offlineColor), /* ... rest ... */ ToolTipLabelFormatter = point => $"Offline: {TimeSpan.FromSeconds(totalOfflineSeconds):hh\\:mm\\:ss} ({offlinePercentage:P1})" });

                    StatePieChartSeries = pieSeries.ToArray();
                    _log.LogInformation($"Pie chart data processed with {pieSeries.Count} slices.");
                }
            }
            catch (Exception ex) { _log.LogError(ex, "Error processing pie chart data"); StatePieChartSeries = Array.Empty<ISeries>(); TotalActiveDuration = TimeSpan.Zero; TotalActivePercentage = 0; TotalAwayDuration = TimeSpan.Zero; TotalAwayPercentage = 0; TotalOfflineDuration = TimeSpan.Zero; TotalOfflinePercentage = 0; }
            finally { OnPropertyChanged(nameof(StatePieChartSeries)); }
        }

        /// <summary>
                /// Prepares data for the Daily Presence Heatmap visualization.
        /// Aggregates summary data, creates weighted points for the heatmap,
        /// and configures the chart axes with Users on X and Dates on Y.
                /// </summary>
                /// <param name="masterUserList">The complete list of users relevant for the current filter.</param>
                /// <param name="summaryData">The collection of summarized report data for the selected period/filters.</param>
        private async Task ProcessPresenceData(List<string> masterUserList, ObservableCollection<SummarizedReportData> summaryData)
        {
            _log.LogInformation("Processing data for Daily Presence Heatmap...");
            _presenceDateMap.Clear();
            _presenceUserMap.Clear();
            PresenceHeatMapSeries = Array.Empty<ISeries>(); // Clear previous series
            // Default axes (will be configured later)
            PresenceXAxes = new Axis[] { new Axis { Name = "User", IsVisible = false } };
            PresenceYAxes = new Axis[] { new Axis { Name = "Date", IsVisible = false } };

            // Initial validation checks
            if (masterUserList == null || !masterUserList.Any())
            {
                _log.LogWarning("No master user list provided for Presence Heatmap. Cannot generate.");
                StatusMessage = "Cannot generate presence map: No users found for selection.";
                OnPropertyChanged(nameof(PresenceHeatMapSeries)); OnPropertyChanged(nameof(PresenceXAxes)); OnPropertyChanged(nameof(PresenceYAxes));
                return;
            }
            if (StartDate > EndDate)
            {
                _log.LogWarning("Invalid date range for Presence Heatmap (Start > End).");
                OnPropertyChanged(nameof(PresenceHeatMapSeries)); OnPropertyChanged(nameof(PresenceXAxes)); OnPropertyChanged(nameof(PresenceYAxes));
                return;
            }

            try
            {
                // Populate user and date maps (Users for X-axis, Dates for Y-axis)
                _presenceUserMap = masterUserList.OrderBy(u => u).ToList(); // Users for X axis labels
                for (DateTime date = StartDate.Date; date <= EndDate.Date; date = date.AddDays(1))
                {
                    _presenceDateMap.Add(date); // Dates for Y axis labels
                }

                if (!_presenceDateMap.Any())
                {
                    _log.LogWarning("No dates found in the selected range for Presence Heatmap.");
                    OnPropertyChanged(nameof(PresenceHeatMapSeries)); OnPropertyChanged(nameof(PresenceXAxes)); OnPropertyChanged(nameof(PresenceYAxes));
                    return;
                }
                _log.LogDebug($"Presence Map: {_presenceUserMap.Count} users (X-axis), {_presenceDateMap.Count} dates (Y-axis).");

                var weightedPoints = new List<WeightedPoint>();
                const double STATUS_ABSENT = 0;
                const double STATUS_PRESENT = 1;

                // --- AGGREGATION STEP (from Phase 1) ---
                var aggregatedSummary = (summaryData ?? new ObservableCollection<SummarizedReportData>())
          .GroupBy(s => new { s.UserID, DateKey = s.Date.Date })
          .Select(g => new SummarizedReportData
          {
              UserID = g.Key.UserID,
              Date = g.Key.DateKey,
              TotalActiveSeconds = g.Sum(x => x.TotalActiveSeconds),
              TotalAwaySeconds = g.Sum(x => x.TotalAwaySeconds),
              TotalOfflineSeconds = g.Sum(x => x.TotalOfflineSeconds),
              ODS = g.FirstOrDefault()?.ODS,
              PartitionKey = g.FirstOrDefault()?.PartitionKey,
              RowKey = g.FirstOrDefault()?.RowKey
          })
          .ToList();
                _log.LogInformation("Aggregation complete. Processing {Count} aggregated records for heatmap dictionary.", aggregatedSummary.Count);

                // Create a lookup dictionary from the *aggregated* data
                var summaryLookup = aggregatedSummary
          .GroupBy(s => s.UserID)
          .ToDictionary(
            g => g.Key,
            g => g.ToDictionary(
              i => i.Date.Date,
              i => i.TotalActiveSeconds
            )
          );

                // --- Generate weighted points (Swapped userIndex and dateIndex) ---
                for (int userIndex = 0; userIndex < _presenceUserMap.Count; userIndex++) // X-coordinate
                {
                    string userName = _presenceUserMap[userIndex];
                    for (int dateIndex = 0; dateIndex < _presenceDateMap.Count; dateIndex++) // Y-coordinate
                    {
                        DateTime currentDate = _presenceDateMap[dateIndex];
                        double currentStatusWeight = STATUS_ABSENT; // Default to Absent

                        // Check lookup for presence
                        if (summaryLookup.TryGetValue(userName, out var userDateSummary) &&
              userDateSummary.TryGetValue(currentDate, out double currentActiveSecondsValue) &&
              currentActiveSecondsValue > 0)
                        {
                            currentStatusWeight = STATUS_PRESENT;
                        }

                        // Add point with SWAPPED coordinates
                        weightedPoints.Add(new WeightedPoint(userIndex, dateIndex, currentStatusWeight));
                    }
                }

                _log.LogInformation($"Generated {weightedPoints.Count} WeightedPoints for the presence map (User=X, Date=Y).");

                // --- Single Day View Contrast Points (Logic remains same, coordinates swapped automatically above) ---
                if (_presenceDateMap.Count == 1)
                {
                    _log.LogInformation("Single day view detected - adding contrast points to help renderer");
                    // Use (0,0) for coordinates as dateIndex is 0 for single day view
                    weightedPoints.Add(new WeightedPoint(0, 0, 0)); // Force low extreme at user 0, date 0
                    weightedPoints.Add(new WeightedPoint(0, 0, 1.1)); // Force high extreme at user 0, date 0
                }

                // --- Heatmap Series Configuration (Color logic remains the same) ---
                var heatSeries = new HeatSeries<WeightedPoint>
                {
                    Values = weightedPoints,
                    ColorStops = new double[] { STATUS_ABSENT, STATUS_ABSENT + 0.01, STATUS_PRESENT - 0.01, STATUS_PRESENT },
                    HeatMap = new[]
          {
            new SKColor(192, 192, 192).AsLvcColor(), // Grey for Absent
                        new SKColor(192, 192, 192).AsLvcColor(), // End of grey range
                        new SKColor(60, 179, 113).AsLvcColor(),  // Start of green range
                        new SKColor(60, 179, 113).AsLvcColor()   // Green for Present
                    },
                    DataLabelsPaint = null // No labels on heatmap cells
                    // TooltipLabelFormatter = ... // Add if needed, remember PrimaryValue is UserIndex, SecondaryValue is DateIndex
                };
                PresenceHeatMapSeries = new ISeries[] { heatSeries };

                // --- ** SWAPPED AXES CONFIGURATION ** ---
                // X-Axis: Users (bottom)
                PresenceXAxes = new Axis[]
        {
          new Axis
          {
            Name = "User",
            Labels = _presenceUserMap.ToArray(), // Use user names
                        LabelsRotation = 90, // Rotate labels for better fit with many users
                        TextSize = 9,        // Reduce text size slightly
                        MinStep = 1,
            ForceStepToMin = true,
            UnitWidth = 1,
                        Position = LiveChartsCore.Measure.AxisPosition.End // Ensure labels are at the bottom
                    }
        };
                // Y-Axis: Dates (left side)
                PresenceYAxes = new Axis[]
        {
          new Axis
          {
            Name = "Date",
            Labels = _presenceDateMap.Select(d => d.ToString("dd MMM")).ToArray(), // Format dates
                        LabelsRotation = 0, // Keep horizontal
                        TextSize = 10,
            MinStep = 1,
            ForceStepToMin = true,
            UnitWidth = 1,
                        Position = LiveChartsCore.Measure.AxisPosition.Start // Ensure labels are on the left
                    }
        };

                _log.LogInformation("Presence Heatmap AXES SWAPPED configuration complete (User=X, Date=Y).");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error processing data for Presence Heatmap.");
                StatusMessage = "Error generating presence map.";
                // Reset chart properties on error
                PresenceHeatMapSeries = Array.Empty<ISeries>();
                PresenceXAxes = new Axis[] { new Axis { Name = "Error", IsVisible = true } };
                PresenceYAxes = new Axis[] { new Axis() };
            }
            finally
            {
                // Ensure UI properties are always updated
                try
                {
                    OnPropertyChanged(nameof(PresenceHeatMapSeries));
                    OnPropertyChanged(nameof(PresenceXAxes));
                    OnPropertyChanged(nameof(PresenceYAxes));

                    // Yield control briefly
                    await Application.Current.Dispatcher.InvokeAsync(() => {
                        _log.LogTrace("Dispatcher InvokeAsync for Heatmap update completed.");
                    }, DispatcherPriority.ContextIdle);
                }
                catch (Exception dispEx)
                {
                    _log.LogError(dispEx, "Error during dispatcher invoke for heatmap update.");
                }
            }
        }

        // Inside TimeReportsViewModel.cs
        private void ProcessDailyActivityStepData(string userName, DateTime date)
        {
            _log.LogInformation($"Processing Step Chart data for User: {userName}, Date: {date:yyyy-MM-dd}");
            const double STATE_LEVEL_OFFLINE = 0; const double STATE_LEVEL_AWAY = 1; const double STATE_LEVEL_ACTIVE = 2;
            var offlineColor = new SKColor(192, 192, 192); var awayColor = new SKColor(255, 152, 0);
            var activeColor = new SKColor(60, 179, 113);
            DailyActivitySeries = Array.Empty<ISeries>(); DateTime dayStart = date.Date; DateTime dayEnd = dayStart.AddDays(1);
            DailyActivityXAxis = new Axis[] { new Axis { Name = "Time of Day", Labeler = value => new DateTime((long)value).ToString("HH:mm"), LabelsRotation = 0, TextSize = 10, UnitWidth = TimeSpan.FromHours(1).Ticks, MinStep = TimeSpan.FromHours(1).Ticks, MinLimit = dayStart.Ticks, MaxLimit = dayEnd.Ticks } };
            DailyActivityYAxis = new Axis[] { new Axis { Name = "State", Labels = new string[] { "Offline", "Away", "Active" }, TextSize = 10, MinLimit = -0.5, MaxLimit = 2.5, MinStep = 1, ForceStepToMin = true, IsVisible = true } };
            try
            {
                // Ensure DetailedReportData is not null before querying
                if (DetailedReportData == null)
                {
                    _log.LogWarning($"DetailedReportData is null. Cannot process step chart for {userName} on {date:yyyy-MM-dd}.");
                    StatusMessage = $"Detailed data not available for {userName} on {date:yyyy-MM-dd}.";
                    // Clear chart properties and return
                    OnPropertyChanged(nameof(DailyActivitySeries));
                    OnPropertyChanged(nameof(DailyActivityXAxis));
                    OnPropertyChanged(nameof(DailyActivityYAxis));
                    return;
                }

                var userDayData = DetailedReportData
                                    .Where(d => d.UserName.Equals(userName, StringComparison.OrdinalIgnoreCase)
                                                && d.StateStartTime.ToLocalTime().Date == date.Date)
                                    .OrderBy(d => d.StateStartTime)
                                    .ToList();

                if (!userDayData.Any())
                {
                    _log.LogWarning($"No detailed data found for {userName} on {date:yyyy-MM-dd} for Step Chart.");
                    StatusMessage = $"No activity details found for {userName} on {date:yyyy-MM-dd}.";
                    OnPropertyChanged(nameof(DailyActivitySeries));
                    OnPropertyChanged(nameof(DailyActivityXAxis));
                    OnPropertyChanged(nameof(DailyActivityYAxis));
                    return;
                }
                _log.LogDebug($"Found {userDayData.Count} detail records for Step Chart view.");

                // *** CHANGE: Use List<DetailedStatePoint> ***
                var stepPoints = new List<DetailedStatePoint>();
                DetailedStatePoint lastPointAdded = null; // Use the custom type

                for (int i = 0; i < userDayData.Count; i++)
                {
                    var detail = userDayData[i];
                    double stateLevel; SKColor stateColor; // stateColor not used in this version, but kept for context
                    if (detail.State.Equals("Active", StringComparison.OrdinalIgnoreCase)) { stateLevel = STATE_LEVEL_ACTIVE; stateColor = activeColor; }
                    else if (detail.State.Equals("Away", StringComparison.OrdinalIgnoreCase)) { stateLevel = STATE_LEVEL_AWAY; stateColor = awayColor; }
                    else { stateLevel = STATE_LEVEL_OFFLINE; stateColor = offlineColor; }

                    DateTime startTimeUtc = detail.StateStartTime; // Get UTC start time
                    long timeTicks = startTimeUtc.Ticks;

                    // *** CHANGE: Create DetailedStatePoint ***
                    var currentPoint = new DetailedStatePoint(
                        timeTicks,
                        stateLevel,
                        detail.State, // Pass state name
                        detail.CalculatedDurationSeconds, // Pass calculated duration
                        startTimeUtc // Pass UTC start time
                    );
                    stepPoints.Add(currentPoint);
                    lastPointAdded = currentPoint;

                    // Handle the end point of the last segment
                    if (i == userDayData.Count - 1 && detail.CalculatedDurationSeconds.HasValue && detail.CalculatedDurationSeconds > 0)
                    {
                        DateTime endTimeUtc = detail.StateStartTime.AddSeconds(detail.CalculatedDurationSeconds.Value);
                        DateTime viewDayEndUtc = dayEnd.ToUniversalTime();
                        if (endTimeUtc > viewDayEndUtc) endTimeUtc = viewDayEndUtc;
                        if (endTimeUtc.Ticks > timeTicks)
                        {
                            // *** CHANGE: Add end point as DetailedStatePoint ***
                            stepPoints.Add(new DetailedStatePoint(
                                endTimeUtc.Ticks,
                                stateLevel,
                                detail.State, // State is the same as the segment
                                null,        // Duration isn't applicable to the *end* point marker
                                endTimeUtc   // Use end time as the time for this point
                            ));
                        }
                    }
                }

                // *** CHANGE: Use StepLineSeries<DetailedStatePoint> and add TooltipLabelFormatter ***
                var stepSeries = new StepLineSeries<DetailedStatePoint>
                {
                    Values = stepPoints,
                    Name = userName,
                    Stroke = new SolidColorPaint(SKColors.DarkGray, 1.5f),
                    Fill = null,
                    GeometrySize = 0,
                    XToolTipLabelFormatter = (chartPoint) =>
                    {
                        var detailedPoint = chartPoint.Model as DetailedStatePoint;
                        if (detailedPoint == null) return "Error";

                        string timeString = TimeZoneInfo.ConvertTimeFromUtc(detailedPoint.StartTimeUtc, TimeZoneInfo.Local).ToString("HH:mm:ss");
                        string durationString = "Ongoing";
                        if (detailedPoint.DurationSeconds.HasValue && detailedPoint.DurationSeconds > 0)
                        {
                            durationString = TimeSpan.FromSeconds(detailedPoint.DurationSeconds.Value).ToString(@"hh\:mm\:ss");
                        }
                        else if (detailedPoint.DurationSeconds.HasValue && detailedPoint.DurationSeconds == 0)
                        {
                            durationString = "0s";
                        }

                        // *** CHANGE HERE: Replace \n with Environment.NewLine ***
                        return $"State: {detailedPoint.State}{Environment.NewLine}Time: {timeString}{Environment.NewLine}Duration: {durationString}";
                    }
                };
                DailyActivitySeries = new ISeries[] { stepSeries };
                _log.LogInformation($"Created StepLineSeries for activity view.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error processing Step Chart data for {userName} on {date:yyyy-MM-dd}.");
                StatusMessage = "Error generating daily activity view."; DailyActivitySeries = Array.Empty<ISeries>();
            }
            finally
            {
                OnPropertyChanged(nameof(DailyActivitySeries));
                OnPropertyChanged(nameof(DailyActivityXAxis));
                OnPropertyChanged(nameof(DailyActivityYAxis));
            }
        }

        private List<DetailedReportData> CalculateDetailedDurations(List<DetailedReportData> rawData)
        {
            if (rawData == null || !rawData.Any()) { _log.LogWarning("CalculateDetailedDurations received no data."); return new List<DetailedReportData>(); }
            _log.LogInformation($"Calculating durations for {rawData.Count} detailed records...");
            var groupedData = rawData.GroupBy(d => new { d.UserName, d.MachineID }).Select(g => g.OrderBy(d => d.StateStartTime).ToList()).ToList();
            DateTime reportStartBoundaryLocal = StartDate.Date; DateTime reportEndBoundaryLocal = EndDate.Date.AddDays(1).AddTicks(-1); DateTime reportEndBoundaryUtc = reportEndBoundaryLocal.ToUniversalTime(); DateTime nowUtc = DateTime.UtcNow;
            foreach (var userMachineGroup in groupedData)
            {
                if (!userMachineGroup.Any()) continue;
                for (int i = 0; i < userMachineGroup.Count; i++)
                {
                    var currentLog = userMachineGroup[i]; DateTime currentStartUtc = currentLog.StateStartTime; DateTime nextStartUtc; string calculationSource;
                    if (currentStartUtc < DateTime.MinValue.AddYears(100) || currentStartUtc > DateTime.MaxValue.AddYears(-100)) { _log.LogWarning($"  Record {i + 1}/{userMachineGroup.Count} (State: {currentLog.State}): Invalid StateStartTime detected: {currentStartUtc:o}. Skipping duration calculation."); currentLog.CalculatedDurationSeconds = null; continue; }
                    if (i + 1 < userMachineGroup.Count) { nextStartUtc = userMachineGroup[i + 1].StateStartTime; calculationSource = "Next Record"; } else { DateTime effectiveEndTimeUtc = nowUtc < reportEndBoundaryUtc ? nowUtc : reportEndBoundaryUtc; nextStartUtc = effectiveEndTimeUtc; calculationSource = $"Last Record (Ends at {effectiveEndTimeUtc:o}, based on Now={nowUtc:o} vs ReportEnd={reportEndBoundaryUtc:o})"; if (currentStartUtc > nextStartUtc) { _log.LogWarning($"  Record {i + 1}/{userMachineGroup.Count} (State: {currentLog.State}): Start time {currentStartUtc:o} is AFTER the effective end time {nextStartUtc:o}. Setting duration to null. {calculationSource}"); currentLog.CalculatedDurationSeconds = null; continue; } }
                    if (nextStartUtc > currentStartUtc) { TimeSpan duration = nextStartUtc - currentStartUtc; currentLog.CalculatedDurationSeconds = duration.TotalSeconds; } else { currentLog.CalculatedDurationSeconds = null; _log.LogWarning($"    -> Invalid time sequence or zero duration (StartUTC >= NextStartUTC). StartUTC={currentStartUtc:o}, NextStartUTC={nextStartUtc:o}. Setting duration to null."); }
                }
            }
            var result = groupedData.SelectMany(group => group).OrderBy(d => d.UserName).ThenBy(d => d.MachineID).ThenBy(d => d.StateStartTime).ToList();
            _log.LogInformation($"Duration calculation complete. Returning {result.Count} processed records.");
            return result;
        }

        // Generic version:
        private void ApplySort<T>(ObservableCollection<T> collection, string columnName, ListSortDirection direction) where T : class
        {
            if (collection == null) return;
            ICollectionView view = CollectionViewSource.GetDefaultView(collection);
            if (view == null || !view.CanSort)
            {
                _log.LogError($"Could not get sortable view for collection of type {typeof(T).Name}");
                return;
            }
            try
            {
                using (view.DeferRefresh())
                {
                    view.SortDescriptions.Clear();
                    view.SortDescriptions.Add(new SortDescription(columnName, direction));
                }
                // No need to call Refresh() explicitly when using DeferRefresh()
                _log.LogInformation($"Applied sort to {typeof(T).Name} View: '{columnName}' {direction}.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, $"Error applying sort '{columnName}' {direction} to {typeof(T).Name} View");
            }
        }

        // --- Filter Data Loading Methods ---

        private async Task LoadOdsPractices()
        {
            try
            {
                OdsPractices.Clear();
                OdsPractices.Add("All"); // Always add "All" option

                // Fetch permitted ODS codes from the service
                var permittedOdsCodes = await _edgeAlertService.GetPermittedOdsAsync();
                if (permittedOdsCodes != null && permittedOdsCodes.Any())
                {
                    var practices = new List<string>();
                    // Convert ODS codes to Practice names using JSONHandler
                    foreach (var odsCode in permittedOdsCodes)
                    {
                        var location = _jsonHandler.GetLocationByODS(odsCode);
                        if (location != null && !string.IsNullOrWhiteSpace(location.Practice))
                        {
                            practices.Add(location.Practice);
                        }
                        else
                        {
                            _log.LogWarning($"Could not find Practice Name for permitted ODS code: {odsCode}");
                        }
                    }
                    // Add distinct, sorted practices to the collection
                    foreach (var practice in practices.Distinct().OrderBy(p => p))
                    {
                        OdsPractices.Add(practice);
                    }
                }

                SelectedOdsPractice = "All"; // Default selection
                _log.LogInformation($"Loaded {OdsPractices.Count - 1} permitted ODS practices for filter.");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error loading permitted ODS practices filter.");
                StatusMessage = "Error loading ODS filter list.";
                // Ensure collection is reset even on error
                OdsPractices.Clear();
                OdsPractices.Add("All");
                SelectedOdsPractice = "All";
            }
        }

        private async Task LoadUserNames()
        {
            // This method is simplified now as the actual loading happens
            // when an ODS practice is selected (in OnSelectedOdsPracticeChanged).
            // We just need to initialize the list with "All".
            UserNames.Clear();
            UserNames.Add("All");
            SelectedUserName = "All"; // Default selection
            _log.LogInformation("User filter initialized with 'All'.");
            await Task.CompletedTask; // Keep async signature for consistency
        }

        // --- Commands ---

        [RelayCommand(CanExecute = nameof(CanLoadReportData))]
        private async Task LoadReportData()
        {
            if (IsBusy) return;
            IsBusy = true;
            StatusMessage = "Loading report data...";
            SummaryReportData.Clear();
            DetailedReportData.Clear();

            // --- Reset dependent data ---
            TotalUsersPresentSummary = "0";
            TotalWorkHoursSummary = "0h 0m";
            TotalAwayTimeSummary = "0h 0m";
            TotalOfflineTimeSummary = "0h 0m";
            StatePieChartSeries = Array.Empty<ISeries>();
            // Reset Heatmap and Drilldown view
            PresenceHeatMapSeries = Array.Empty<ISeries>();
            PresenceXAxes = new Axis[] { new Axis { Name = "Date", IsVisible = false } };
            PresenceYAxes = new Axis[] { new Axis { Name = "User", IsVisible = false } };
            _presenceDateMap.Clear(); _presenceUserMap.Clear();
            IsDailyActivityVisible = false; SelectedActivityUser = null; SelectedActivityDate = null;
            DailyActivitySeries = Array.Empty<ISeries>(); DailyActivityXAxis = new Axis[] { new Axis() }; DailyActivityYAxis = new Axis[] { new Axis() };
            // --- End Reset ---

            string odsFilterToSend = null;
            if (SelectedOdsPractice != "All" && !string.IsNullOrEmpty(SelectedOdsPractice))
            {
                var location = _jsonHandler.GetLocationByPractice(SelectedOdsPractice);
                if (location != null && !string.IsNullOrEmpty(location.ODS)) { odsFilterToSend = location.ODS; }
                else
                {
                    _log.LogError($"LoadReportData: Could not find ODS code for selected practice '{SelectedOdsPractice}'. Aborting load.");
                    StatusMessage = $"Error: Invalid ODS Practice selected.";
                    IsBusy = false; return;
                }
            }
            string userFilterToSend = (SelectedUserName == "All" || string.IsNullOrEmpty(SelectedUserName)) ? null : SelectedUserName;

            _log.LogInformation($"Loading Time Report Data: User='{userFilterToSend ?? "All"}', ODS='{odsFilterToSend ?? "All Permitted"}', Start='{StartDate:yyyy-MM-dd}', End='{EndDate:yyyy-MM-dd}'");
            bool summarySuccess = false;
            bool detailSuccess = false;
            List<string> masterUserList = null;

            try
            {
                // Fetch Master User List
                StatusMessage = "Loading user list for presence map...";
                if (SelectedOdsPractice == "All")
                {
                    _log.LogInformation("ODS Practice 'All' selected. Master user list for presence map will be derived from loaded summary data.");
                    masterUserList = new List<string>();
                }
                else if (!string.IsNullOrEmpty(odsFilterToSend))
                {
                    masterUserList = await _edgeAlertService.GetPermittedUsersForOdsAsync(odsFilterToSend);
                    if (masterUserList == null)
                    {
                        _log.LogError($"Failed to load permitted users for ODS '{odsFilterToSend}'. Presence map may be incomplete or fail.");
                        StatusMessage = $"Warning: Could not load full user list for ODS {odsFilterToSend}. Presence map might be incomplete.";
                        masterUserList = new List<string>();
                    }
                    else
                    {
                        _log.LogInformation($"Loaded {masterUserList.Count} users for ODS '{odsFilterToSend}' for presence map.");
                    }
                }
                else
                {
                    _log.LogWarning("No specific ODS selected and 'All' logic not fully implemented for master user list. Presence map may be inaccurate.");
                    masterUserList = new List<string>();
                }

                // Load Summary Data
                StatusMessage = "Loading summary data...";
                var summaryDataResult = await _edgeAlertService.GetSummarizedReportDataAsync(StartDate, EndDate, userFilterToSend, odsFilterToSend);
                if (summaryDataResult != null && summaryDataResult.Any())
                {
                    // Assign new collection instance
                    SummaryReportData = new ObservableCollection<SummarizedReportData>(summaryDataResult);
                    OnPropertyChanged(nameof(SummaryReportData)); // Notify UI of the new instance

                    _log.LogInformation($"Loaded {SummaryReportData.Count} summary records.");
                    summarySuccess = true;
                    if (SelectedOdsPractice == "All")
                    {
                        masterUserList = SummaryReportData.Select(s => s.UserID).Distinct().OrderBy(u => u).ToList();
                        _log.LogInformation($"Derived master user list for 'All' ODS from summary data: {masterUserList.Count} users.");
                    }

                    // Process data *after* collection is populated
                    UpdateSummaryCards(SummaryReportData);
                    ProcessSummaryDataForPieChart(SummaryReportData);
                    await ProcessPresenceData(masterUserList, SummaryReportData);

                    // --- >> ADDED: Apply Default Summary Sort << ---
                    // Apply default/last sort to the newly loaded summary data on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        // Use the Summary View's last sort settings
                        ApplySort(SummaryReportData, _lastSummarySortColumn, _lastSummarySortDirection);
                        _log.LogInformation($"Applied initial/last sort to Summary View: '{_lastSummarySortColumn}' {_lastSummarySortDirection}.");
                    }, DispatcherPriority.ContextIdle); // Use ContextIdle or Background

                }
                else
                {
                    _log.LogWarning("Failed to load summary report data or no data found.");
                    // Ensure UI updates even when no data is found
                    SummaryReportData = new ObservableCollection<SummarizedReportData>(); // Set to empty collection
                    OnPropertyChanged(nameof(SummaryReportData)); // Notify UI
                    UpdateSummaryCards(new List<SummarizedReportData>());
                    ProcessSummaryDataForPieChart(new ObservableCollection<SummarizedReportData>());
                    await ProcessPresenceData(new List<string>(), new ObservableCollection<SummarizedReportData>());
                }

                // Load Detailed Data
                StatusMessage = "Loading detailed data...";
                string machineFilterToSend = null;
                var rawDetailedData = await _edgeAlertService.GetDetailedReportDataAsync(StartDate, EndDate, userFilterToSend, machineFilterToSend, odsFilterToSend);
                if (rawDetailedData != null && rawDetailedData.Any())
                {
                    _log.LogInformation($"Loaded {rawDetailedData.Count} raw detailed records. Processing for display...");
                    var processedDetailedData = CalculateDetailedDurations(rawDetailedData);

                    // Assign new collection instance
                    DetailedReportData = new ObservableCollection<DetailedReportData>(processedDetailedData);
                    OnPropertyChanged(nameof(DetailedReportData)); // Notify UI of the new instance

                    detailSuccess = true;
                    _log.LogInformation($"Populated DetailedReportData collection with {DetailedReportData.Count} processed records for display.");

                    // Apply Detailed Sort (using Detailed View's last sort settings) on UI thread
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        ApplySort(DetailedReportData, _lastDetailedSortColumn, _lastDetailedSortDirection);
                        _log.LogInformation($"Applied initial/last sort to Detailed View: '{_lastDetailedSortColumn}' {_lastDetailedSortDirection}.");
                    }, DispatcherPriority.ContextIdle);
                }
                else
                {
                    _log.LogWarning("Failed to load detailed report data or no data found.");
                    // Ensure UI updates even when no data is found
                    DetailedReportData = new ObservableCollection<DetailedReportData>(); // Set to empty collection
                    OnPropertyChanged(nameof(DetailedReportData)); // Notify UI
                }

                // Update Final Status
                if (summarySuccess || detailSuccess)
                {
                    StatusMessage = $"Report data loaded. Summary: {SummaryReportData.Count}, Detailed: {DetailedReportData.Count}.";
                }
                else
                {
                    StatusMessage = "Failed to load report data. Please check logs or filter criteria.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "Error loading report data.";
                _log.LogError(ex, "Exception during LoadReportData.");
                // Ensure UI updates on error
                SummaryReportData = new ObservableCollection<SummarizedReportData>();
                OnPropertyChanged(nameof(SummaryReportData));
                DetailedReportData = new ObservableCollection<DetailedReportData>();
                OnPropertyChanged(nameof(DetailedReportData));
                UpdateSummaryCards(new List<SummarizedReportData>());
                ProcessSummaryDataForPieChart(new ObservableCollection<SummarizedReportData>());
                await ProcessPresenceData(new List<string>(), new ObservableCollection<SummarizedReportData>());
            }
            finally
            {
                IsBusy = false;
            }
        }

        private bool CanLoadReportData() { return !IsBusy && StartDate <= EndDate && EndDate <= DateTime.Today; }

        // --- Add New Export Command ---
        [RelayCommand(CanExecute = nameof(CanExportReport))]
        private async Task ExportReport()
        {
            _log.LogInformation("ExportReportCommand started.");
            if (IsBusy || SummaryReportData == null || DetailedReportData == null)
            {
                _log.LogWarning("ExportReportCommand execution skipped: Already busy or report data not loaded.");
                StatusMessage = "Load report data before exporting.";
                // --- PHASE 4: User feedback if export cannot start ---
                MessageBox.Show(StatusMessage, "Export Prerequisite", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // --- PHASE 4: Ensure CanExecute updates when IsBusy changes ---
            // We already have [NotifyCanExecuteChangedFor(nameof(LoadReportDataCommand))] on IsBusy
            // Add similar notification for ExportReportCommand if needed, though RelayCommand often handles this.
            // Explicitly call NotifyCanExecuteChanged if issues arise.
            // ExportReportCommand.NotifyCanExecuteChanged(); // Usually not needed with ObservableProperty/RelayCommand

            IsBusy = true;
            StatusMessage = "Exporting report data... Please wait."; // More informative busy message

            List<SummarizedReportData> summaryDataToExport = new List<SummarizedReportData>(SummaryReportData);
            List<DetailedReportData> detailedDataToExport = new List<DetailedReportData>(DetailedReportData)
                .OrderBy(d => d.StateStartTime)
                .ToList();

            _log.LogInformation($"Processing {summaryDataToExport.Count} summary and {detailedDataToExport.Count} detailed records for export.");

            string fullPath = string.Empty; // Define outside try for use in messages
            string fileName = string.Empty;

            try
            {
                // Generate file path
                string reportType = "Time_Logging";
                fileName = $"{reportType}-{DateTime.Now:yyyyMMdd}.xlsx";
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                fullPath = Path.Combine(desktopPath, fileName);

                _log.LogInformation($"Export target path: {fullPath}");

                // Instantiate and call the service
                var exportService = new ExcelExportService(null); // Pass null logger
                await exportService.ExportTimeLogDataAsync(summaryDataToExport, detailedDataToExport, fullPath);

                // --- PHASE 4: Success Message ---
                StatusMessage = $"Export successful: {fileName} saved to Desktop.";
                _log.LogInformation(StatusMessage);
                MessageBox.Show(StatusMessage, "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            }
            // --- PHASE 4: Specific Error Handling ---
            catch (IOException ioEx) // Catch file access issues (e.g., file open)
            {
                _log.LogError(ioEx, "IO Error during ExportReportCommand execution. File: {FilePath}", fullPath);
                StatusMessage = "Export failed: File may be open or inaccessible.";
                MessageBox.Show($"{StatusMessage}\n\nError: {ioEx.Message}\n\nPlease ensure the file '{fileName}' is not open in another application and you have permission to write to the Desktop.", "Export Error - File Access", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex) // Catch other general errors
            {
                _log.LogError(ex, "Error during ExportReportCommand execution.");
                StatusMessage = "Export failed. An unexpected error occurred.";
                MessageBox.Show($"{StatusMessage}\n\nError: {ex.Message}\n\nCheck the application logs for more details.", "Export Error - Unexpected", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                IsBusy = false;
                // Reset status message or leave the success/failure message? Optional.
                // StatusMessage = "Ready";
                _log.LogInformation("ExportReportCommand finished.");
                // ExportReportCommand.NotifyCanExecuteChanged(); // Usually not needed
            }
        }

        // Update CanExportReport method (already checks IsBusy)
        private bool CanExportReport()
        {
            bool summaryDataExists = SummaryReportData != null && SummaryReportData.Count > 0;
            bool detailedDataExists = DetailedReportData != null && DetailedReportData.Count > 0;
            bool canExecute = !IsBusy && summaryDataExists && detailedDataExists;

            // --- Add/Uncomment Detailed Logging ---
            _log?.LogInformation( // Changed to Information for easier visibility
                "CanExportReport evaluated: {CanExecuteResult} (IsBusy={IsBusyFlag}, SummaryDataExists={SummaryExistsFlag} [Count={SummaryCount}], DetailedDataExists={DetailedExistsFlag} [Count={DetailedCount}])",
                canExecute,
                IsBusy,
                summaryDataExists,
                SummaryReportData?.Count ?? -1, // Use null conditional operator for safety
                detailedDataExists,
                DetailedReportData?.Count ?? -1 // Use null conditional operator for safety
            );
            // --- End Logging ---

            return canExecute;
        }

        // ** STUB METHOD FOR PHASE 1 - Replace later **
        /// <summary>
        /// Creates a basic, empty Excel file with the specified sheets.
        /// This is a placeholder for Phase 1 to verify file creation and saving.
        /// Will be replaced by calls to a dedicated ExcelExportService.
        /// </summary>
        /// <param name="filePath">The full path where the file should be saved.</param>
        private async Task CreateStubExcelFile(string filePath)
        {
            await Task.Run(() => // Run on background thread
            {
                _log.LogInformation($"Creating stub Excel file at: {filePath}");
                using (var workbook = new XLWorkbook())
                {
                    workbook.AddWorksheet("Summary");
                    workbook.AddWorksheet("Details");
                    workbook.SaveAs(filePath);
                }
                _log.LogInformation($"Stub Excel file created successfully.");
            });
        }

        [RelayCommand]
        private void SortDetailedView(string columnName)
        {
            if (string.IsNullOrEmpty(columnName) || DetailedReportData == null) return;
             ListSortDirection direction;
            if (columnName == _lastDetailedSortColumn)
            {
                direction = (_lastDetailedSortDirection == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending;
             }
            else
            {
                direction = ListSortDirection.Ascending; // Default to Ascending for new column
            }
            ApplySort(DetailedReportData, columnName, direction); // Use ApplySort for DetailedReportData
            _lastDetailedSortColumn = columnName;
            _lastDetailedSortDirection = direction;
         }

        // Command for Summary View Sorting
        [RelayCommand]
        private void SortSummaryView(string columnName)
        {
            if (string.IsNullOrEmpty(columnName) || SummaryReportData == null) return;

            ListSortDirection direction;
            // Determine new direction based on last sort for summary view
            if (columnName == _lastSummarySortColumn)
            {
                direction = (_lastSummarySortDirection == ListSortDirection.Ascending) ? ListSortDirection.Descending : ListSortDirection.Ascending;
            }
            else
            {
                direction = ListSortDirection.Ascending; // Default to Ascending for new column
            }

            ApplySort(SummaryReportData, columnName, direction); // Use ApplySort for SummaryReportData

            // Update last sorted state for summary view
            _lastSummarySortColumn = columnName;
            _lastSummarySortDirection = direction;
        }

        [RelayCommand]
        private async Task SetDateRange(string range)
        {
            _log.LogInformation($"SetDateRange command executed with parameter: {range}");
            var today = DateTime.Today; DateTime newStartDate = StartDate; DateTime newEndDate = EndDate; bool dateChanged = false;
            switch (range) { case "Today": newStartDate = today; newEndDate = today; break; case "Yesterday": newStartDate = today.AddDays(-1); newEndDate = today.AddDays(-1); break; case "Last 7 Days": newStartDate = today.AddDays(-6); newEndDate = today; break; case "Last 30 Days": newStartDate = today.AddDays(-29); newEndDate = today; break; case "This Month": newStartDate = new DateTime(today.Year, today.Month, 1); newEndDate = today; break; case "Last Month": var firstDayLastMonth = new DateTime(today.Year, today.Month, 1).AddMonths(-1); newStartDate = firstDayLastMonth; newEndDate = firstDayLastMonth.AddMonths(1).AddDays(-1); break; default: _log.LogWarning($"Unknown range parameter in SetDateRange: {range}"); return; }
            if (StartDate != newStartDate) { SetProperty(ref _startDate, newStartDate, nameof(StartDate)); dateChanged = true; }
            if (EndDate != newEndDate) { SetProperty(ref _endDate, newEndDate, nameof(EndDate)); dateChanged = true; }
            if (dateChanged) { await TriggerAutoLoad("Date Range Preset Change"); } else { _log.LogInformation("Dates did not change, skipping automatic data reload."); }
        }

        [RelayCommand]
        private void DrillDownToActivityChart(object commandParameter)
        {
            ChartPoint chartPoint = null; if (commandParameter is IEnumerable<ChartPoint> pointsEnum) { chartPoint = pointsEnum.FirstOrDefault(); _log.LogDebug($"Command parameter is IEnumerable<ChartPoint>, using first point: {chartPoint?.Coordinate.ToString() ?? "null"}"); } else { _log.LogWarning($"Received unexpected data type from heatmap click: {commandParameter?.GetType().Name ?? "null"}"); }
            if (chartPoint != null) { int dateIndex = (int)chartPoint.Coordinate.SecondaryValue; int userIndex = (int)chartPoint.Coordinate.PrimaryValue; if (userIndex >= 0 && userIndex < _presenceUserMap.Count && dateIndex >= 0 && dateIndex < _presenceDateMap.Count) { SelectedActivityUser = _presenceUserMap[userIndex]; SelectedActivityDate = _presenceDateMap[dateIndex]; _log.LogInformation($"Drilling down to Activity Step Chart view for User: {SelectedActivityUser}, Date: {SelectedActivityDate:yyyy-MM-dd}"); ProcessDailyActivityStepData(SelectedActivityUser, SelectedActivityDate.Value); IsDailyActivityVisible = true; } else { _log.LogWarning($"Invalid indices extracted from heatmap click: UserIndex={userIndex}, DateIndex={dateIndex}"); } } else { _log.LogWarning($"Could not extract a valid ChartPoint from the command parameter."); }
        }

        [RelayCommand]
        private void GoBackToHeatmap()
        {
            _log.LogInformation("Returning to Heatmap view."); IsDailyActivityVisible = false; SelectedActivityUser = null; SelectedActivityDate = null;
            DailyActivitySeries = Array.Empty<ISeries>(); DailyActivityXAxis = new Axis[] { new Axis() }; DailyActivityYAxis = new Axis[] { new Axis() };
        }

        // --- Lifecycle/Message Handlers ---
        public async void Receive(MainViewModelInitialisedMessage message) // Changed to async void
        {
            _log.LogInformation("Received MainViewModelInitialisedMessage. Starting TimeReportsViewModel initialization.");

            // *** Ensure execution is on the UI thread ***
            //await Application.Current.Dispatcher.InvokeAsync(async () => // Use Dispatcher
            //{
            //    _log.LogInformation("Executing LoadInitialData on UI thread.");
            //    await LoadInitialData(); // Await the async method called on the UI thread
            //});
        }

        /// <summary>
        /// Initializes the view model's data by loading filters and triggering
        /// the initial data load, but only if it hasn't been initialized already
        /// in the current application session.
        /// </summary>
        public async Task InitializeViewAsync()
        {
            lock (_initLock)
            {
                if (_isViewInitialized)
                {
                    _log.LogInformation("InitializeViewAsync: View already initialized. Skipping.");
                    return; // Already initialized, do nothing
                }
                _isViewInitialized = true; // Set flag immediately within lock
                _log.LogInformation("InitializeViewAsync: First activation detected. Proceeding with initialization.");
            }

            // Perform the actual load outside the lock
            // Ensure execution is on the UI thread as LoadInitialData modifies collections
            await Application.Current.Dispatcher.InvokeAsync(async () =>
            {
                _log.LogInformation("InitializeViewAsync: Calling LoadInitialData on UI thread.");
                await LoadInitialData(); // Call the existing method to load filters and trigger data load
            });
        }

        private async Task LoadInitialData() // Make LoadInitialData async Task
        {
            IsBusy = true;
            StatusMessage = "Loading filters...";
            try
            {
                // No Dispatcher needed here as the caller (Receive) ensures UI thread
                LoadOdsPractices(); // This modifies OdsPractices
                await LoadUserNames(); // This modifies FilteredUserNames
                StatusMessage = "Filters loaded. Select user to view permissions.";
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Error loading initial data for Report Access Management.");
                StatusMessage = "Error loading filters.";
            }
            finally
            {
                IsBusy = false;
            }
        }
    }

    public class DetailedStatePoint : ObservablePoint
    {
        public string State { get; set; }
        public double? DurationSeconds { get; set; }
        public DateTime StartTimeUtc { get; set; } // Store original UTC time

        // Constructor
        public DetailedStatePoint(double xTimeTicks, double yStateLevel, string state, double? durationSeconds, DateTime startTimeUtc)
            : base(xTimeTicks, yStateLevel) // Initialize base LiveCharts point
        {
            State = state;
            DurationSeconds = durationSeconds;
            StartTimeUtc = startTimeUtc;
        }
    }
}