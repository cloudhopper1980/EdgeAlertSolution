# Expert Detail Questions - Asset Management Tab

## Q6: Should we extend the existing AssetManagementViewModel.cs at EdgeAlertSignalClient/ViewModels/ to add CRUD operations?
**Default if unknown:** Yes (maintains architectural consistency and leverages existing service injection)

## Q7: Should CSV import/export functionality use the ClosedXML.Excel pattern from ExcelExportService.cs for consistency?
**Default if unknown:** Yes (maintains consistent dependencies and patterns across the application)

## Q8: For the Keyboard/Mouse bulk asset types, should the quantity field replace the SerialNumber field in the AssetItem model or be added as a separate property?
**Default if unknown:** Add as separate property (maintains data model consistency and allows for optional serial numbers)

## Q9: Should the asset type dropdown be populated from a hardcoded enum or fetched from Azure Table Storage for dynamic management?
**Default if unknown:** Hardcoded enum (simpler implementation, matches requirements which specify a fixed list)

## Q10: Should the enhanced AssetManagementView.xaml replace the existing read-only implementation completely or extend it?
**Default if unknown:** Replace completely (user confirmed complete replacement and current view is too basic)