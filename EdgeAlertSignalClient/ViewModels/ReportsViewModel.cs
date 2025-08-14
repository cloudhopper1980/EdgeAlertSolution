using CommunityToolkit.Mvvm.ComponentModel;
using EdgeAlertSignalClient.Services;
using System.Collections.ObjectModel;
using Microsoft.Extensions.Logging;
using CommunityToolkit.Mvvm.Messaging;
using EdgeAlertSignalClient.Message;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using WTable = DocumentFormat.OpenXml.Wordprocessing.Table;
using WTableCell = DocumentFormat.OpenXml.Wordprocessing.TableCell;
using WTableRow = DocumentFormat.OpenXml.Wordprocessing.TableRow;
using WParagraph = DocumentFormat.OpenXml.Wordprocessing.Paragraph;
using WRun = DocumentFormat.OpenXml.Wordprocessing.Run;
using WBold = DocumentFormat.OpenXml.Wordprocessing.Bold;
using System;
using static EdgeAlertSignalClient.Services.JoinTables;
using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Tab;
using System.ComponentModel;
using System.DirectoryServices;
using System.Windows.Data;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;
using ClosedXML.Excel;
using System.Runtime.InteropServices;
using System.Management;

namespace EdgeAlertSignalClient.ViewModels
{
    public partial class ReportsViewModel : ObservableObject,
                                            IRecipient<UserListMessage>,
                                            IRecipient<AlertHistoryMessage>,
                                            IRecipient<SystemInfoMessage>
    {
        private readonly ILogger<MainViewModel> _log;

        // We'll populate this on update and use this to display
        [ObservableProperty]
        public ObservableCollection<EdgeUserViewModel>? _edgeUserListView;

        [ObservableProperty]
        public ObservableCollection<EdgeUserViewModel>? _edgeFireDrillListView;

        [ObservableProperty]
        public ObservableCollection<AlertHistory> _alertHistory;

        [ObservableProperty]
        public ObservableCollection<AlertHistory> _alertHistoryListView;

        [ObservableProperty]
        public ObservableCollection<SystemInfoService> _systemInfoList;

        [ObservableProperty]
        public ObservableCollection<SystemInfoService> _activeSystemInfoList;

        [ObservableProperty]
        public ObservableCollection<SystemInfoService> _archivedSystemInfoList;

        [ObservableProperty]
        public int _activeTab;

        [ObservableProperty]
        public bool _buttonsEnabled;

        [ObservableProperty]
        public DateTime _dateFrom;

        [ObservableProperty]
        public DateTime _dateTo;

        [ObservableProperty]
        public string _sortColumnUserList;

        [ObservableProperty]
        public string _sortColumnAlertHistory;

        [ObservableProperty]
        public string _sortColumnSystemInfo;

        [ObservableProperty]
        public ListSortDirection _sortDirectionUserList;

        [ObservableProperty]
        public ListSortDirection _sortDirectionAlertHistory;

        [ObservableProperty]
        public ListSortDirection _sortDirectionSystemInfo;

        [ObservableProperty]
        public string _selectedExportType;

        [ObservableProperty]
        public int _exportTypeIndex;

        private Dictionary<string, ListSortDirection> _sortingDirections = new Dictionary<string, ListSortDirection>();

        // Create CollectionViews for your ObservableCollections
        private ICollectionView _edgeUserListViewView;
        private ICollectionView _edgeFireDrillListViewView;
        private ICollectionView _alertHistoryListViewView;
        private ICollectionView _systemInfoListViewView;

        public ReportsViewModel(ILogger<MainViewModel> log,
                                IEdgeAlertService edgeAlertService)
        {
            _log = log;
            EdgeUserListView = new ObservableCollection<EdgeUserViewModel>();
            EdgeFireDrillListView = new ObservableCollection<EdgeUserViewModel>();
            AlertHistoryListView = new ObservableCollection<AlertHistory>();
            SystemInfoList = new ObservableCollection<SystemInfoService>();
            ActiveSystemInfoList = new ObservableCollection<SystemInfoService>();
            ArchivedSystemInfoList = new ObservableCollection<SystemInfoService>();
            DateFrom = DateTime.Today;
            DateTo = DateTime.Today;

            // Initialize sorting properties
            SortColumnUserList = "UserName"; // Default sorting column
            SortDirectionUserList = ListSortDirection.Ascending; // Default sorting direction
            SortColumnAlertHistory = "Raised By"; // Default sorting column
            SortDirectionAlertHistory = ListSortDirection.Ascending; // Default sorting direction
            SortColumnSystemInfo = "Hostname"; // Default sorting column
            SortDirectionSystemInfo = ListSortDirection.Ascending; // Default sorting direction

            // Create CollectionViews for your ObservableCollections
            _edgeUserListViewView = CollectionViewSource.GetDefaultView(EdgeUserListView);
            _edgeFireDrillListViewView = CollectionViewSource.GetDefaultView(EdgeFireDrillListView);
            _alertHistoryListViewView = CollectionViewSource.GetDefaultView(AlertHistoryListView);
            _systemInfoListViewView = CollectionViewSource.GetDefaultView(SystemInfoList);

            // Subscribe to Messages
            WeakReferenceMessenger.Default.Register<UserListMessage>(this);
            WeakReferenceMessenger.Default.Register<AlertHistoryMessage>(this);
            WeakReferenceMessenger.Default.Register<SystemInfoMessage>(this);

            SelectedExportType = "Word";
            ExportTypeIndex = 0;

            _log.LogInformation("ReportsViewModel: Initialised");
        }

        public async void Receive(UserListMessage message)
        {
            // Ensure UI updates happen on the dispatcher thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                EdgeUserListView.Clear();
                EdgeFireDrillListView.Clear();

                // Copy from message.Value to EdgeUserListView
                foreach (var user in message.Value)
                {
                    EdgeUserListView.Add(user);
                    EdgeFireDrillListView.Add(user);
                }
                ButtonsEnabled = EdgeUserListView != null && EdgeUserListView.Count > 0;
            });
        }

        public void Receive(AlertHistoryMessage message)
        {
            // Ensure UI updates happen on the dispatcher thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Store history when Azure updates
                AlertHistory = message.Value;
                FilterAlertHistory();
            });
        }

        public void Receive(SystemInfoMessage message)
        {
            // Ensure UI updates happen on the dispatcher thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                SystemInfoList.Clear();
                ActiveSystemInfoList.Clear();
                ArchivedSystemInfoList.Clear();

                foreach (var systemInfo in message.Value)
                {
                    // If LastOnline is older than 12 months, ignore this system
                    if (IsOlderThan12Months(systemInfo.LastOnline))
                    {
                        continue; // Skip this systemInfo as it is older than 12 months
                    }

                    SystemInfoList.Add(systemInfo);

                    // Split the system info based on the LastOnline date.
                    if (IsArchivedSystem(systemInfo.LastOnline))
                    {
                        ArchivedSystemInfoList.Add(systemInfo);
                    }
                    else
                    {
                        ActiveSystemInfoList.Add(systemInfo);
                    }
                }
            });
        }

        private bool IsArchivedSystem(string lastOnline)
        {
            if (string.Equals(lastOnline, "Online", StringComparison.OrdinalIgnoreCase))
            {
                // If the system is currently online, it is not archived
                return false;
            }

            if (DateTime.TryParse(lastOnline, out DateTime lastOnlineDate))
            {
                // Consider machines that haven't logged on in the last 90 days as archived
                return (DateTime.UtcNow - lastOnlineDate).TotalDays > 90;
            }
            return false;
        }

        private bool IsOlderThan12Months(string lastOnline)
        {
            if (string.Equals(lastOnline, "Online", StringComparison.OrdinalIgnoreCase))
            {
                // If the system is currently online, it is not outdated
                return false;
            }

            if (DateTime.TryParse(lastOnline, out DateTime lastOnlineDate))
            {
                // Consider machines that haven't logged on in the last 12 months as outdated
                return (DateTime.UtcNow - lastOnlineDate).TotalDays > 365;
            }
            return true; // If parsing fails, assume it's outdated and should be ignored
        }

        partial void OnDateFromChanged(DateTime value)
        {
            FilterAlertHistory();
        }

        partial void OnDateToChanged(DateTime value)
        {
            FilterAlertHistory();
        }

        [RelayCommand]
        public void SortColumn(string parameter)
        {
            if (string.IsNullOrEmpty(parameter))
                return;

            // Split the parameter into column name and table type
            var parts = parameter.Split(',');
            string columnName = parts[0];
            string tableType = parts.Length > 1 ? parts[1] : null;

            // Determine the sorting direction
            ListSortDirection newDirection = ListSortDirection.Ascending;

            if (_sortingDirections.ContainsKey(columnName))
            {
                var currentDirection = _sortingDirections[columnName];
                newDirection = currentDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }

            if (string.IsNullOrEmpty(tableType))
            {
                // Default behavior for single-table reports
                if (ActiveTab == 0) // Users tab
                {
                    SortObservableCollection(EdgeUserListView, columnName, newDirection);
                }
                else if (ActiveTab == 1) // Fire Drill tab
                {
                    SortObservableCollection(EdgeFireDrillListView, columnName, newDirection);
                }
                else if (ActiveTab == 2) // Alert History tab
                {
                    SortObservableCollection(AlertHistoryListView, columnName, newDirection);
                }
                else if (ActiveTab == 3) // System Info tab (assume Active by default if no type specified)
                {
                    SortObservableCollection(ActiveSystemInfoList, columnName, newDirection);
                }
            }
            else if (tableType == "Active")
            {
                SortObservableCollection(ActiveSystemInfoList, columnName, newDirection);
            }
            else if (tableType == "Archived")
            {
                SortObservableCollection(ArchivedSystemInfoList, columnName, newDirection);
            }

            // Store the sorting direction for the column
            _sortingDirections[columnName] = newDirection;
        }

        private void SortObservableCollection<T>(ObservableCollection<T> collection, string propertyName, ListSortDirection sortDirection)
        {
            if (collection != null && !string.IsNullOrEmpty(propertyName))
            {
                var propertyInfo = typeof(T).GetProperty(propertyName);

                if (propertyInfo != null)
                {
                    IEnumerable<T> sortedCollection;

                    if (propertyName.EndsWith(" GB", StringComparison.OrdinalIgnoreCase))
                    {
                        if (sortDirection == ListSortDirection.Ascending)
                        {
                            sortedCollection = collection.OrderBy(item =>
                            {
                                var value = propertyInfo.GetValue(item, null) as string;
                                if (!string.IsNullOrEmpty(value) && value.EndsWith(" GB", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Remove " GB", parse the numeric part, and return it for sorting
                                    var numericPart = value.Substring(0, value.Length - 3);
                                    if (double.TryParse(numericPart, out var numericValue))
                                    {
                                        return numericValue;
                                    }
                                }
                                // Default value for non-numeric or invalid data
                                return double.MinValue;
                            });
                        }
                        else
                        {
                            sortedCollection = collection.OrderByDescending(item =>
                            {
                                var value = propertyInfo.GetValue(item, null) as string;
                                if (!string.IsNullOrEmpty(value) && value.EndsWith(" GB", StringComparison.OrdinalIgnoreCase))
                                {
                                    // Remove " GB", parse the numeric part, and return it for sorting
                                    var numericPart = value.Substring(0, value.Length - 3);
                                    if (double.TryParse(numericPart, out var numericValue))
                                    {
                                        return numericValue;
                                    }
                                }
                                // Default value for non-numeric or invalid data
                                return double.MinValue;
                            });
                        }
                    }
                    else
                    {
                        // For other columns, perform default alphanumeric sorting
                        if (sortDirection == ListSortDirection.Ascending)
                        {
                            sortedCollection = collection.OrderBy(item =>
                            {
                                return propertyInfo.GetValue(item, null);
                            });
                        }
                        else
                        {
                            sortedCollection = collection.OrderByDescending(item =>
                            {
                                return propertyInfo.GetValue(item, null);
                            });
                        }
                    }

                    // Create a new ObservableCollection and add the sorted items to it
                    var newCollection = new ObservableCollection<T>(sortedCollection);

                    // Clear the original collection and add the items from the new collection
                    collection.Clear();
                    foreach (var item in newCollection)
                    {
                        collection.Add(item);
                    }
                }
                else
                {
                    // Debugging message for invalid property name
                    Debug.WriteLine($"Invalid property name: {propertyName}");
                }
            }
            else
            {
                // Debugging message for null collection or empty property name
                Debug.WriteLine("Invalid collection or property name.");
            }
        }

        private void FilterAlertHistory()
        {
            // If we haven't yet populated the data, ignore
            if (AlertHistory == null) return;

            AlertHistoryListView.Clear();

            // Temporary list to hold sortable items
            List<AlertHistory> sortableList = new List<AlertHistory>();

            // Assuming your DateFrom and DateTo properties are already populated with the desired DateTime values.
            DateTime dateFrom = DateFrom;
            // Update dateTo to include the current time, ensuring you get data up to the current moment
            DateTime dateTo = DateTime.Today == DateTo.Date ? DateTime.Now : DateTo;

            foreach (var alertHistory in AlertHistory)
            {
                if (DateTime.TryParseExact(alertHistory.DateFrom, "dd/MM/yyyy HH:mm:ss", new DateTimeFormatInfo
                {
                    ShortDatePattern = "dd/MM/yyyy",
                    LongTimePattern = "HH:mm:ss"
                }, DateTimeStyles.None, out DateTime alertDateFrom))
                {
                    if (alertDateFrom >= dateFrom && alertDateFrom <= dateTo)
                    {
                        sortableList.Add(alertHistory);
                    }
                }
            }

            // Sort the list by DateFrom in descending order
            var sortedList = sortableList.OrderByDescending(alert => DateTime.ParseExact(alert.DateFrom, "dd/MM/yyyy HH:mm:ss", new DateTimeFormatInfo
            {
                ShortDatePattern = "dd/MM/yyyy",
                LongTimePattern = "HH:mm:ss"
            })).ToList();

            // Add sorted items to your ObservableCollection or whatever collection type AlertHistoryListView is
            foreach (var sortedItem in sortedList)
            {
                AlertHistoryListView.Add(sortedItem);
            }
        }

        [RelayCommand]
        public void Refresh()
        {
            // Refresh Report data by calling MainView
            WeakReferenceMessenger.Default.Send(new MainViewModelMessage("ReportView"));
        }

        [RelayCommand]
        public void Export()
        {
            // Default to Excel if no valid type is provided
            if (SelectedExportType.Equals("Word", StringComparison.OrdinalIgnoreCase))
            {
                ExportToWordBasedOnActiveTab();
            }
            else
            {
                ExportToExcelBasedOnActiveTab();
            }
        }

        [RelayCommand]
        public void Print()
        {
            if (!IsDefaultPrinterOnline())
            {
                MessageBox.Show("The default printer is currently offline. Please check your printer settings and try again.", "Printer Offline", MessageBoxButton.OK, MessageBoxImage.Error);
                return; // Exit the method if the printer is offline
            }

            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            tempFilePath = SelectedExportType.Equals("Word", StringComparison.OrdinalIgnoreCase) ?
                Path.ChangeExtension(tempFilePath, ".docx") :
                Path.ChangeExtension(tempFilePath, ".xlsx");

            // Default to Excel if no valid type is provided
            if (SelectedExportType.Equals("Word", StringComparison.OrdinalIgnoreCase))
            {
                ExportToWordBasedOnActiveTab(tempFilePath);
            }
            else
            {
                ExportToExcelBasedOnActiveTab(tempFilePath);
            }
            MessageBox.Show("Sent job to printer.", "Sent Job", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ExportToWordBasedOnActiveTab(string filePath = "")
        {
            string exportedFilePath = string.Empty;
            try
            {
                switch (ActiveTab)
                {
                    case 0:
                        // Logic for exporting "Users" data
                        exportedFilePath = ExportToWord($"Users List - {DateTime.Now.ToString("dd/MM/yyyy")}",
                                    "Users_List",
                                    EdgeUserListView,
                                    edgeUser => new string[] { edgeUser.UserName, edgeUser.HostName, edgeUser.Room, edgeUser.LoggedIn, edgeUser.LastOnline },
                                    new string[] { "Name", "Host Name", "Room", "Logged In", "Last Online" },
                                    users => users.GroupBy(u => u.ODS),
                                    5,
                                    filePath);
                        break;
                    case 1:
                        // Existing logic for exporting EdgeUserListView
                        exportedFilePath = ExportToWord($"Staff Fire List - {DateTime.Now.ToString("dd/MM/yyyy")}",
                                    "Staff_Fire_List",
                                    EdgeFireDrillListView,
                                    edgeUser => new string[] { edgeUser.UserName, edgeUser.HostName, edgeUser.Room, edgeUser.LoggedIn, edgeUser.LastOnline },
                                    new string[] { "Name", "Host Name", "Room", "Logged In", "Last Online" },
                                    users => users.GroupBy(u => u.ODS),
                                    5,
                                    filePath);
                        break;
                    case 2:
                        // Add logic for exporting AlertHistoryListView
                        exportedFilePath = ExportToWord($"Alert History List - {DateTime.Now.ToString("dd/MM/yyyy")}",
                                    "Alert_History_Audit",
                                    AlertHistoryListView,
                                    alertHistory => new string[] { alertHistory.RaisedWhen, alertHistory.RaisedBy, alertHistory.Device, alertHistory.Location, alertHistory.Responder, alertHistory.TimeToRespond },
                                    new string[] { "Raised When", "Raised By", "Device", "Location", "Responder", "Time To Respond" },
                                    alerts => alerts.GroupBy(a => a.Practice),
                                    6,
                                    filePath);
                        break;
                    case 3:
                        // Export both Active and Archived System Info separately
                        exportedFilePath = ExportSystemInfoToWord(filePath);
                        break;
                    default:
                        _log.LogError($"Unknown tab: {ActiveTab}");
                        break;
                }

                if (string.IsNullOrEmpty(filePath))
                {
                    MessageBox.Show($"Data exported successfully to {exportedFilePath}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                if (!string.IsNullOrEmpty(filePath))
                {
                    PrintAndDeleteFile(filePath);
                }
            }
            catch (IOException ex) when (ex.Message.Contains("being used by another process"))
            {
                MessageBox.Show("The file is currently in use by another process. Please close the file and try again.", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                ButtonsEnabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to export to Word: {ex.Message}", "Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                ButtonsEnabled = true;
            }
        }

        private string ExportSystemInfoToWord(string filePath = "")
        {
            string activeFilePath = ExportToWord($"Active System Info - {DateTime.Now:dd/MM/yyyy}",
                                "Active_System_Info",
                                ActiveSystemInfoList,
                                sysInfo => new string[] { sysInfo.HostName, sysInfo.SerialNumber, sysInfo.IpAddress, sysInfo.Room, sysInfo.UserName, sysInfo.OperatingSystem, sysInfo.HardDriveTotalSpace, sysInfo.HardDriveFreeSpace, sysInfo.RamCapacity, sysInfo.Manufacturer, sysInfo.Model, sysInfo.LoggedIn, sysInfo.LastOnline },
                                new string[] { "Host Name", "Serial", "IP Address", "Room", "Name", "Operating System", "HD Total (GB)", "HD Free (GB)", "RAM (GB)", "Manufacturer", "Model", "Logged In", "Last Online" },
                                systemInfo => systemInfo.GroupBy(a => a.ODS),
                                13,
                                filePath);

            string archivedFilePath = ExportToWord($"Archived System Info - {DateTime.Now:dd/MM/yyyy}",
                                "Archived_System_Info",
                                ArchivedSystemInfoList,
                                sysInfo => new string[] { sysInfo.HostName, sysInfo.SerialNumber, sysInfo.IpAddress, sysInfo.Room, sysInfo.UserName, sysInfo.OperatingSystem, sysInfo.HardDriveTotalSpace, sysInfo.HardDriveFreeSpace, sysInfo.RamCapacity, sysInfo.Manufacturer, sysInfo.Model, sysInfo.LoggedIn, sysInfo.LastOnline },
                                new string[] { "Host Name", "Serial", "IP Address", "Room", "Name", "Operating System", "HD Total (GB)", "HD Free (GB)", "RAM (GB)", "Manufacturer", "Model", "Logged In", "Last Online" },
                                systemInfo => systemInfo.GroupBy(a => a.ODS),
                                13,
                                filePath);

            return $"{activeFilePath}, {archivedFilePath}";
        }

        public string ExportToWord<T>(string title,
                                    string fileNamePrefix,
                                    IEnumerable<T> records,
                                    Func<T, string[]> convertToRow,
                                    string[] headers,
                                    Func<IEnumerable<T>, IEnumerable<IGrouping<string, T>>> groupBy,
                                    int columnSpan,
                                    string filePath)
        {
            _log.LogInformation($"ReportViewModel: {title}: Exporting to Word");
            ButtonsEnabled = false;

            string fullPath;

            if (filePath == string.Empty)
            {
                string desktopPath = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                string fileName = $"{fileNamePrefix}-{DateTime.Now.ToString("yyyyMMdd")}.docx";
                fullPath = Path.Combine(desktopPath, fileName);
            }
            else
            {
                fullPath = filePath;
            }

            using (WordprocessingDocument wordDocument = WordprocessingDocument.Create(fullPath, WordprocessingDocumentType.Document))
            {
                MainDocumentPart mainPart = wordDocument.AddMainDocumentPart();
                mainPart.Document = new DocumentFormat.OpenXml.Wordprocessing.Document();
                DocumentFormat.OpenXml.Wordprocessing.Body body = new DocumentFormat.OpenXml.Wordprocessing.Body();
                mainPart.Document.Append(body);

                // Same as ExportUsersToWord
                SectionProperties sectionProps = new SectionProperties();
                PageSize pageSize = new PageSize() { Width = (UInt32Value)16838U, Height = (UInt32Value)11906U, Orient = PageOrientationValues.Landscape };
                sectionProps.Append(pageSize);
                body.Append(sectionProps);

                // Title setup remains the same
                WParagraph titleParagraph = new WParagraph();
                ParagraphProperties paragraphProps = new ParagraphProperties();
                paragraphProps.Append(new Justification() { Val = JustificationValues.Center });
                titleParagraph.Append(paragraphProps);
                WRun titleRun = new WRun();
                RunProperties runProps = new RunProperties();
                runProps.Append(new WBold());
                runProps.Append(new FontSize() { Val = "40" });
                titleRun.Append(runProps);
                Text titleText = new Text(title);
                titleRun.Append(titleText);
                titleParagraph.Append(titleRun);
                body.Append(titleParagraph);
                WParagraph emptyParagraph = new WParagraph();
                body.Append(emptyParagraph);

                // Table setup remains similar but the headers change
                WTable table = new WTable();
                TableProperties props = new TableProperties(
                    new TableStyle { Val = "TableGrid" },
                    new TableWidth { Type = TableWidthUnitValues.Pct, Width = "5000" }
                );

                // Add borders to the table
                TableBorders tblBorders = new TableBorders(
                    new TopBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new BottomBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new LeftBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new RightBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideHorizontalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 },
                    new InsideVerticalBorder { Val = new EnumValue<BorderValues>(BorderValues.Single), Size = 4 }
                );

                props.Append(tblBorders);
                table.AppendChild(props);

                // Create and add header row with blue background
                WTableRow headerRow = new WTableRow();
                foreach (var header in headers)
                {
                    WTableCell headerCell = CreateCell(header, true, "DEEAF6"); // True indicates it's a header
                    headerRow.Append(headerCell);
                }
                table.Append(headerRow);

                // Group records
                var groupedRecords = groupBy(records);

                foreach (var group in groupedRecords)
                {
                    // Insert a row for the group name before the grouped items.
                    WTableRow groupRow = new WTableRow();
                    WTableCell groupCell = CreateCell($"Practice: {group.Key}", true, "D3D3D3"); // Gray background for the group row
                    groupRow.Append(groupCell);
                    // Span the group name across all columns
                    TableCellProperties cellProperties = groupCell.Elements<TableCellProperties>().First();
                    cellProperties.Append(new GridSpan { Val = columnSpan });
                    table.Append(groupRow);

                    // Now add the items in the group.
                    foreach (var record in group)
                    {
                        WTableRow row = new WTableRow();
                        string[] cellValues = convertToRow(record);

                        foreach (var cellValue in cellValues)
                        {
                            WTableCell cell = CreateCell(cellValue, false);  // false indicates it's not a header
                            row.Append(cell);
                        }

                        table.Append(row);
                    }
                }

                body.Append(table);
            }

            ButtonsEnabled = true;
            return fullPath;
        }

        private WTableCell CreateCell(string content, bool isHeader, string backgroundColor = null)
        {
            WTableCell cell = new WTableCell();
            TableCellProperties cellProps = new TableCellProperties();

            // Set vertical alignment to center for all cells.
            TableCellVerticalAlignment verticalAlignment = new TableCellVerticalAlignment() { Val = TableVerticalAlignmentValues.Center };
            cellProps.Append(verticalAlignment);

            // Create paragraph with zero line spacing before and after
            ParagraphProperties paragraphProperties = new ParagraphProperties();
            SpacingBetweenLines spacing = new SpacingBetweenLines() { Before = "0", After = "0" };
            paragraphProperties.Append(spacing);

            // Set text alignment based on whether it's a header or not.
            Justification justification = new Justification() { Val = isHeader ? JustificationValues.Center : JustificationValues.Left };
            paragraphProperties.Append(justification);

            WParagraph paragraph = new WParagraph(paragraphProperties);
            WRun run = new WRun();
            Text text = new Text(content);
            run.Append(text);
            paragraph.Append(run);
            cell.Append(paragraph);

            if (isHeader && !string.IsNullOrEmpty(backgroundColor))
            {
                // Set background color for header cells.
                Shading shading = new Shading()
                {
                    Val = ShadingPatternValues.Clear,
                    Fill = backgroundColor,
                    Color = "auto"
                };
                cellProps.Append(shading);
            }

            cell.Append(cellProps);
            return cell;
        }

        private void ExportToExcelBasedOnActiveTab(string filePath = "")
        {
            string reportType = GetReportTypeByActiveTab();
            switch (ActiveTab)
            {
                case 3:
                    // Export both Active and Archived System Info separately
                    ExportSystemInfoToExcel(filePath);
                    break;
                default:
                    IEnumerable<string[]> rowsData;
                    string[] headers;

                    // Decide on headers and data preparation based on the active tab
                    switch (ActiveTab)
                    {
                        case 0:
                            headers = new string[] { "Location", "Name", "Host Name", "Room", "Logged In", "Last Online" };
                            rowsData = EdgeUserListView.Select(item => new string[] {
                        item.ODS ?? string.Empty,
                        item.UserName ?? string.Empty,
                        item.HostName ?? string.Empty,
                        item.Room ?? string.Empty,
                        item.LoggedIn?.ToString() ?? string.Empty,
                        item.LastOnline?.ToString() ?? string.Empty
                    });
                            ExportDataToExcel(reportType, rowsData, headers, filePath);
                            break;
                            // Other cases...
                    }
                    break;
            }
        }

        private void ExportSystemInfoToExcel(string filePath = "")
        {
            string activeReportType = "Active_System_Info";
            string archivedReportType = "Archived_System_Info";

            // Headers for system info
            string[] headers = new string[] { "Location", "Host Name", "Serial", "IP Address", "Room", "Name", "Operating System", "HD Total (GB)", "HD Free (GB)", "RAM (GB)", "Manufacturer", "Model", "Logged In", "Last Online" };

            // Active system info
            IEnumerable<string[]> activeRowsData = ActiveSystemInfoList.Select(item => new string[]
            {
        item.ODS ?? string.Empty,
        item.HostName ?? string.Empty,
        item.SerialNumber ?? string.Empty,
        item.IpAddress ?? string.Empty,
        item.Room ?? string.Empty,
        item.UserName ?? string.Empty,
        item.OperatingSystem ?? string.Empty,
        item.HardDriveTotalSpace ?? string.Empty,
        item.HardDriveFreeSpace ?? string.Empty,
        item.RamCapacity ?? string.Empty,
        item.Manufacturer ?? string.Empty,
        item.Model ?? string.Empty,
        item.LoggedIn?.ToString() ?? string.Empty,
        item.LastOnline?.ToString() ?? string.Empty
            });

            ExportDataToExcel(activeReportType, activeRowsData, headers, filePath);

            // Archived system info
            IEnumerable<string[]> archivedRowsData = ArchivedSystemInfoList.Select(item => new string[]
            {
        item.ODS ?? string.Empty,
        item.HostName ?? string.Empty,
        item.SerialNumber ?? string.Empty,
        item.IpAddress ?? string.Empty,
        item.Room ?? string.Empty,
        item.UserName ?? string.Empty,
        item.OperatingSystem ?? string.Empty,
        item.HardDriveTotalSpace ?? string.Empty,
        item.HardDriveFreeSpace ?? string.Empty,
        item.RamCapacity ?? string.Empty,
        item.Manufacturer ?? string.Empty,
        item.Model ?? string.Empty,
        item.LoggedIn?.ToString() ?? string.Empty,
        item.LastOnline?.ToString() ?? string.Empty
            });

            ExportDataToExcel(archivedReportType, archivedRowsData, headers, filePath);
        }

        private string GetReportTypeByActiveTab()
        {
            return ActiveTab switch
            {
                0 => "Users_List",
                1 => "Fire_Drill_List",
                2 => "Alert_History",
                3 => "System_Info",
                _ => "Export"
            };
        }

        private double? ParseToDouble(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return null;

            // Remove "GB" and any non-numeric except dot for decimal
            var numericString = new string(input.Where(c => char.IsDigit(c) || c == '.').ToArray());

            if (double.TryParse(numericString, out double result))
                return result;

            return null; // Or handle the case where parsing is not possible
        }

        private void ExportDataToExcel(string reportType, IEnumerable<string[]> rowsData, string[] headers, string filePath = "")
        {
            ButtonsEnabled = false;

            string path;

            if (filePath == string.Empty)
            {
                string fileName = $"{reportType}-{DateTime.Now:yyyyMMdd}.xlsx";
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), fileName);
            }
            else
            {
                path = filePath;
            }

            try
            {
                using (var workbook = new XLWorkbook())
                {
                    var worksheet = workbook.AddWorksheet("Exported Data");
                    // Configure page setup
                    worksheet.PageSetup.PaperSize = XLPaperSize.A4Paper;
                    worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
                    worksheet.PageSetup.FitToPages(1, 0); // Fit to one page wide, unlimited pages tall

                    // Adding headers and setting header style
                    for (int i = 0; i < headers.Length; i++)
                    {
                        var cell = worksheet.Cell(1, i + 1);
                        cell.Value = headers[i];
                        cell.Style.Fill.BackgroundColor = XLColor.LightGray;
                        cell.Style.Font.Bold = true;
                    }

                    int row = 2;
                    foreach (var rowData in rowsData)
                    {
                        for (int col = 0; col < rowData.Length; col++)
                        {
                            if (headers[col] == "HD Total (GB)" || headers[col] == "HD Free (GB)" || headers[col] == "RAM (GB)")
                            {
                                var numericValue = ParseToDouble(rowData[col]);
                                worksheet.Cell(row, col + 1).Value = numericValue;
                                worksheet.Cell(row, col + 1).Style.NumberFormat.Format = "0.00"; // Format for decimal places
                            }
                            else
                            {
                                worksheet.Cell(row, col + 1).Value = rowData[col];
                            }
                        }
                        row++;
                    }

                    worksheet.Range(1, 1, 1, headers.Length).SetAutoFilter();
                    worksheet.Columns().AdjustToContents();

                    workbook.SaveAs(path);

                    if (string.IsNullOrEmpty(filePath))
                    {
                        MessageBox.Show($"Data exported successfully to {path}", "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        PrintAndDeleteFile(filePath);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"An error occurred while saving the file: {ex.Message}", "Error Saving File", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                ButtonsEnabled = true;
            }
        }

        private void PrintAndDeleteFile(string filePath)
        {
            ProcessStartInfo info = new ProcessStartInfo(filePath)
            {
                Verb = "print",
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            };
            using (Process process = new Process { StartInfo = info })
            {
                process.Start();
                process.WaitForExit();
            }

            SafelyDeleteTempFile(filePath);
        }

        private void SafelyDeleteTempFile(string filePath)
        {
            try
            {
                File.Delete(filePath);
            }
            catch (IOException ex)
            {
                _log.LogWarning($"Temporary file {filePath} could not be deleted immediately due to an IO Exception: {ex.Message}. It might be deleted later.");
            }
            catch (UnauthorizedAccessException ex)
            {
                _log.LogError($"Lack of permission to delete temporary file {filePath}: {ex.Message}");
            }
        }

        public bool IsDefaultPrinterOnline()
        {
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Printer WHERE Default=True"))
            {
                foreach (var printer in searcher.Get())
                {
                    return !Convert.ToBoolean(printer["WorkOffline"]);
                }
            }
            return false; // In case there's no default printer found
        }

    }
}
