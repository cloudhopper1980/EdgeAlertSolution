# Requirements Specification - Asset Management Tab Enhancement

## Problem Statement

The EdgeAlert WPF application currently has a basic, read-only Asset Management tab that needs to be completely replaced with a comprehensive asset management system. The new system must support CRUD operations, CSV import/export, filtering, searching, and management of various non-PC hardware assets with special handling for bulk items like keyboards and mice.

## Solution Overview

Replace the existing basic implementation with a full-featured asset management system using a master-detail layout. The solution will leverage existing MVVM patterns, Azure Table Storage, and Excel processing capabilities while introducing new functionality for bulk asset management and dynamic asset type configuration.

## Functional Requirements

### F1: Asset Type Management
- **F1.1**: Store asset types in Azure Table Storage for dynamic management
- **F1.2**: Support standard asset types: Monitor, Printer, Label Printer, Scanner, Docking Station, External Hard Drive, Network Switch, Router, UPS, Webcam
- **F1.3**: Support bulk asset types: Keyboard (Bulk), Mouse (Bulk) with quantity fields instead of individual serial numbers
- **F1.4**: Asset type dropdown populated from Azure Table Storage
- **F1.5**: AssetType entity includes IsBulk boolean property for robust bulk asset identification

### F2: CRUD Operations
- **F2.1**: Create new assets with form validation
- **F2.2**: Edit existing assets in detail panel
- **F2.3**: Soft delete assets with DeletedAt timestamp
- **F2.4**: Recover deleted assets within 1 month
- **F2.5**: Archive view toggle to show/hide deleted assets

### F3: Data Management
- **F3.1**: All assets stored in new dedicated Azure Table Storage table
- **F3.2**: Global serial number uniqueness across all ODS locations (enforced server-side)
- **F3.3**: Required ODS Location assignment for each asset
- **F3.4**: Optional fields: Manufacturer, Model, Purchase Date, Warranty Expiry, Assigned To, Asset Location, Notes
- **F3.5**: Auto-generated Asset ID (GUID)
- **F3.6**: Automatic CreatedAt/UpdatedAt timestamps
- **F3.7**: Consider separate lookup table for serial number uniqueness if performance issues arise

### F4: Bulk Asset Handling
- **F4.1**: Separate Quantity property for Keyboard (Bulk) and Mouse (Bulk) types
- **F4.2**: Optional serial number for bulk assets
- **F4.3**: UI shows quantity field instead of serial number for bulk types

### F5: CSV Import/Export
- **F5.1**: CSV template download using ClosedXML.Excel pattern
- **F5.2**: CSV import with inline validation error display
- **F5.3**: Required fields enforcement during import
- **F5.4**: Serial number uniqueness validation during import (authoritative server-side validation)
- **F5.5**: ODS Location validation against existing list
- **F5.6**: CSV export of filtered asset data
- **F5.7**: Background threading for CSV processing with progress indicators

### F6: Filtering and Search
- **F6.1**: Advanced filters by Asset Type, Status, ODS Location (server-side implementation)
- **F6.2**: Search functionality across all text fields (server-side for large datasets)
- **F6.3**: Column sorting on all data grid columns
- **F6.4**: Asset type grouping (e.g., group all monitors, printers, etc.)
- **F6.5**: Efficient pagination for large datasets

### F7: User Interface
- **F7.1**: Master-detail layout with 2:3 column ratio
- **F7.2**: DataGrid master list with filtering controls
- **F7.3**: Editable detail panel with appropriate controls
- **F7.4**: Toolbar with Create/Edit/Delete/Import/Export buttons
- **F7.5**: Status management: Active/Inactive/Archived dropdown
- **F7.6**: Date picker controls for dates
- **F7.7**: ComboBox dropdowns for Asset Type and ODS Location

## Technical Requirements

### T1: Architecture Compliance
- **T1.1**: Extend existing `AssetManagementViewModel.cs` with new functionality
- **T1.2**: Replace existing `AssetManagementView.xaml` completely
- **T1.3**: Use `ObservableValidator` base class for validation
- **T1.4**: Follow CommunityToolkit.Mvvm patterns with `[ObservableProperty]` and `IAsyncRelayCommand`
- **T1.5**: Maintain dependency injection patterns

### T2: Data Layer
- **T2.1**: Create separate services: `AssetTypesService.cs` and `AssetCsvService.cs` to prevent "god class"
- **T2.2**: Extend `AssetTableService.cs` with core asset operations only
- **T2.3**: Add CSV processing methods following ClosedXML.Excel patterns in dedicated service
- **T2.4**: Implement client-side preliminary validation with server-side authoritative validation
- **T2.5**: Create new Azure Table Storage table for asset types
- **T2.6**: Update `AssetItem.cs` model with Quantity property
- **T2.7**: Monitor Azure Table Storage performance for global serial number queries

### T3: Backend Integration
- **T3.1**: Enhance `EdgeAlertFunc/AssetManagement/AssetFunctions.cs` with additional endpoints
- **T3.2**: Add asset type management endpoints
- **T3.3**: Implement CSV upload/download endpoints
- **T3.4**: Add server-side filtering and pagination endpoints for performance
- **T3.5**: Implement authoritative server-side validation for all critical operations
- **T3.6**: Add server-side serial number uniqueness enforcement across all partitions

### T4: UI Framework
- **T4.1**: Use existing theme resources from `Theme/TabTheme.xaml`
- **T4.2**: Follow ComboBox patterns from `FeatureSwitchManagementView.xaml`
- **T4.3**: Implement percentage width converters for responsive layout
- **T4.4**: Use CollectionViewSource for grouping and filtering

### T5: File Processing
- **T5.1**: Use ClosedXML.Excel library for consistency
- **T5.2**: Implement background threading with `Task.Run()`
- **T5.3**: Add structured logging with `ILogger<T>`
- **T5.4**: Exception handling with re-throw pattern

## Implementation Hints

### File Modifications Required

1. **Models/AssetItem.cs**
   ```csharp
   public int? Quantity { get; set; } // For bulk asset types
   // Note: IsBulkAsset logic moved to AssetType entity for better data model
   ```

   **Models/AssetType.cs** (New)
   ```csharp
   public class AssetType
   {
       public string TypeId { get; set; }
       public string Name { get; set; }
       public bool IsBulk { get; set; } // Replaces string parsing approach
       public bool IsActive { get; set; }
   }
   ```

2. **ViewModels/AssetManagementViewModel.cs**
   - Add ObservableValidator base class
   - Add CRUD commands: CreateAssetCommand, EditAssetCommand, DeleteAssetCommand
   - Add filtering properties: FilterText, SelectedAssetTypeFilter, SelectedStatusFilter
   - Add CSV commands: ImportCsvCommand, ExportCsvCommand, DownloadTemplateCommand
   - Add collections: AssetTypes, OdsLocations, StatusOptions

3. **Views/AssetManagementView.xaml**
   - Complete replacement with toolbar section
   - Enhanced DataGrid with filtering controls
   - Editable detail panel with ComboBoxes and DatePickers
   - Archive toggle button

4. **Services/AssetTableService.cs**
   - Add filtering methods: GetAssetsByTypeAsync, GetAssetsByStatusAsync
   - Add CSV methods: ImportFromCsvAsync, ExportToCsvAsync
   - Add validation: ValidateSerialNumberUniquenessAsync

5. **New Files Needed**
   - **Models/AssetType.cs** - Asset type entity with IsBulk property
   - **Services/AssetTypesService.cs** - Asset type management (separate from AssetTableService)
   - **Services/AssetCsvService.cs** - CSV processing utilities (separate from AssetTableService)
   - **EdgeAlertFunc/AssetManagement/AssetTypeFunctions.cs** - Server-side asset type endpoints

### Key Patterns to Follow

1. **MVVM Command Pattern**
   ```csharp
   [RelayCommand]
   private async Task CreateAssetAsync()
   {
       // Implementation
   }
   ```

2. **Validation Pattern**
   ```csharp
   [Required]
   [ObservableProperty]
   private string _serialNumber = string.Empty;
   ```

3. **ComboBox Binding Pattern**
   ```xaml
   <ComboBox ItemsSource="{Binding AssetTypes}"
             SelectedItem="{Binding SelectedAsset.AssetType, Mode=TwoWay}"
             IsEnabled="{Binding IsBusy, Converter={StaticResource InverseBooleanConverter}}"/>
   ```

## Implementation Strategy

### Phased Rollout (Recommended)
**Phase 1: Core CRUD Operations**
- Basic asset management with CRUD operations
- Simple filtering and master-detail UI
- Asset type management with IsBulk property
- Server-side validation for serial number uniqueness

**Phase 2: Advanced Features** 
- CSV import/export functionality
- Advanced filtering with server-side implementation
- Pagination for large datasets
- Archive view and recovery operations

**Phase 3: Performance Optimizations**
- Implement separate serial number lookup table if needed
- Advanced querying and performance monitoring
- UI responsiveness optimizations

### Risk Management

**High Priority Risks:**
- **R1**: Azure Table Storage performance for global serial number queries
  - *Mitigation*: Monitor performance, implement separate lookup table if needed
- **R2**: UI thread blocking during CSV operations
  - *Mitigation*: All long-running operations use `Task.Run()` with progress indicators
- **R3**: Data integrity with concurrent users
  - *Mitigation*: ETag-based optimistic concurrency control

**Medium Priority Risks:**
- **R4**: Service class complexity ("god class" anti-pattern)
  - *Mitigation*: Separate AssetTypesService and AssetCsvService from AssetTableService
- **R5**: Client vs server validation consistency
  - *Mitigation*: Server-side validation is authoritative, client-side is preliminary only

## Acceptance Criteria

1. ✅ Complete replacement of existing read-only implementation
2. ✅ CRUD operations for all asset types with server-side validation
3. ✅ Bulk asset support with quantity fields and IsBulk property
4. ✅ CSV import/export with background threading and inline validation
5. ✅ Asset type management through Azure Table Storage with IsBulk support
6. ✅ Server-side global serial number uniqueness enforcement
7. ✅ Archive view with recovery functionality
8. ✅ Server-side filtering and pagination for performance
9. ✅ Master-detail UI layout with responsive design
10. ✅ Separated service architecture preventing "god class" anti-pattern

## Assumptions

- **A1**: Existing Azure Table Storage connection strings and configuration will be reused
- **A2**: Current ODS location list structure will remain unchanged
- **A3**: ClosedXML.Excel library is already available in the project
- **A4**: Asset serial numbers that are not unique globally will be rejected during import/creation
- **A5**: Bulk assets (Keyboard/Mouse) will be identified via IsBulk property in AssetType entity
- **A6**: Deleted assets older than 1 month will be handled by separate cleanup processes
- **A7**: Asset type management will be available to admin users through existing admin interface patterns