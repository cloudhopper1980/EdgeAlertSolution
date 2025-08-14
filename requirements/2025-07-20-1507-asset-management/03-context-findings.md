# Context Findings - Asset Management Tab

## Existing Implementation Analysis

### Current State
The EdgeAlertSolution already has a **basic** Asset Management implementation that needs to be **enhanced** to meet the new requirements:

#### Existing WPF Client Structure
- **ViewModel**: `EdgeAlertSignalClient/ViewModels/AssetManagementViewModel.cs` - Basic implementation with `ObservableCollection<AssetItem>` and `LoadAssetsCommand`
- **View**: `EdgeAlertSignalClient/Views/AssetManagementView.xaml` - Master-detail layout with read-only DataGrid and detail panel
- **Model**: `EdgeAlertSignalClient/Models/AssetItem.cs` - Complete model matching requirements fields
- **Service**: `EdgeAlertSignalClient/Services/AssetTableService.cs` - Azure Table Storage service with CRUD operations
- **Navigation**: Already integrated in `MainWindow.xaml:144-151` as "Asset Management" tab

#### Existing Azure Functions Backend
- **Functions**: `EdgeAlertFunc/AssetManagement/AssetFunctions.cs` - HTTP endpoints for Create, Get, Update, Delete, and Recover
- **Entity**: `EdgeAlertFunc/AssetManagement/AssetEntity.cs` - Table entity for Azure Table Storage
- **Service**: `EdgeAlertFunc/AssetManagement/AssetTableService.cs` - Server-side table operations

### Architecture Patterns Identified

#### MVVM Implementation Pattern
- Uses `CommunityToolkit.Mvvm.ComponentModel.ObservableObject` base class
- `[ObservableProperty]` attributes for automatic property notification
- `IAsyncRelayCommand` for async operations
- Constructor dependency injection of services

#### Master-Detail UI Pattern
- Grid with 2-column layout (2* for master, 3* for detail)
- DataGrid for master list with `ItemsSource="{Binding Assets}"`
- Detail panel with `DataContext="{Binding SelectedAsset}"`
- Current implementation is **read-only** - needs editing capabilities

#### Azure Table Storage Pattern
- Partition Key = ODS Location
- Row Key = Asset ID (GUID)
- `ITableEntity` implementation with `ETag` for optimistic concurrency
- Soft delete using `DeletedAt` timestamp
- Converter methods between model and entity

#### Navigation Pattern
- RadioButton navigation in `MainWindow.xaml`
- Command binding to ViewModels
- Current navigation command: `AssetManagementTabViewModel.ShowAssetManagementCommand`

### Key Gaps to Address

#### Missing CRUD UI Features
1. **Create/Edit Forms**: No editing capabilities in current UI
2. **Delete/Archive Operations**: No delete buttons or confirmation dialogs
3. **CSV Import/Export**: No file operations or validation
4. **Filtering/Searching**: No search or filter controls
5. **Asset Type Management**: No dropdown for asset types
6. **ODS Location Integration**: No dropdown for ODS validation

#### Missing Asset Types
Current implementation doesn't specify supported asset types. Requirements specify:
- Monitor, Printer, Label Printer, Scanner, Docking Station
- External Hard Drive, Network Switch, Router, UPS, Webcam
- **Special handling needed**: Keyboard/Mouse with quantity fields instead of individual serial numbers

#### Missing UI Controls
1. **Dropdown Controls**: For Asset Type and ODS Location
2. **Date Pickers**: For Purchase Date and Warranty Expiry
3. **Status Management**: Active/Inactive/Archived dropdown
4. **Validation**: Required field validation and unique serial number checking
5. **Archive View**: Separate view for deleted assets with recovery options

### Implementation Strategy

#### Phase 1: Enhance Existing ViewModel
Extend `AssetManagementViewModel.cs` with:
- Add/Edit/Delete commands
- Asset type and ODS location lists
- Validation logic
- CSV import/export commands

#### Phase 2: Enhance UI
Modify `AssetManagementView.xaml`:
- Add toolbar with Create/Edit/Delete/Import/Export buttons
- Convert detail panel to editable form
- Add dropdowns and date pickers
- Implement filtering and search controls

#### Phase 3: Service Enhancements
Extend `AssetTableService.cs`:
- CSV processing methods
- Advanced filtering and searching
- Validation methods
- Asset type enumeration

#### Phase 4: Backend Integration
Enhance Azure Functions:
- CSV upload/download endpoints
- Asset type management
- ODS location validation
- Advanced querying capabilities

### Additional Patterns Discovered

#### Excel/CSV Processing Pattern
- Uses `ClosedXML.Excel` library for Excel exports (`ExcelExportService.cs`)
- Background threading with `Task.Run()` for heavy operations
- Structured logging with `ILogger<T>` for operations
- Column auto-sizing with `Columns().AdjustToContents()`
- Exception handling with re-throw for command-level handling

#### Validation Pattern
- Uses `ObservableValidator` base class (inherits from `ObservableObject`)
- `System.ComponentModel.DataAnnotations` attributes for validation
- Real-time validation with property change notifications

#### ComboBox/Dropdown Pattern
- `ItemsSource` binding to collections in ViewModel
- `SelectedItem` with two-way binding
- `IsEnabled` binding with boolean converter for busy states
- Example: `FeatureSwitchManagementView.xaml` uses this pattern

#### UI Control Patterns
- TabControl with `SelectedIndex` binding for tab management
- ListView with CollectionViewSource for grouping
- GridView columns with percentage width converters
- Command binding for column sorting functionality
- Theme resources merged from `Theme/TabTheme.xaml`

### Files Requiring Modification
1. `EdgeAlertSignalClient/ViewModels/AssetManagementViewModel.cs` - Major enhancements
2. `EdgeAlertSignalClient/Views/AssetManagementView.xaml` - UI overhaul  
3. `EdgeAlertSignalClient/Services/AssetTableService.cs` - CSV and validation features
4. `EdgeAlertFunc/AssetManagement/AssetFunctions.cs` - Additional endpoints
5. New files needed:
   - CSV processing utilities extending ExcelExportService patterns
   - Asset type enumeration
   - Validation services following ObservableValidator pattern

### Technical Constraints
- Must maintain existing MVVM patterns
- Must use CommunityToolkit.Mvvm conventions with ObservableValidator
- Must integrate with existing Azure Table Storage structure
- Must maintain soft delete pattern with 1-month recovery window
- Must validate against existing ODS location list
- Must follow ClosedXML pattern for CSV export functionality
- Must use existing theme resources and control patterns