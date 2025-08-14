# Expert Detail Answers - Asset Management Tab

## Q6: Should we extend the existing AssetManagementViewModel.cs at EdgeAlertSignalClient/ViewModels/ to add CRUD operations?
**Answer:** Yes

## Q7: Should CSV import/export functionality use the ClosedXML.Excel pattern from ExcelExportService.cs for consistency?
**Answer:** Yes

## Q8: For the Keyboard/Mouse bulk asset types, should the quantity field replace the SerialNumber field in the AssetItem model or be added as a separate property?
**Answer:** Add separate

## Q9: Should the asset type dropdown be populated from a hardcoded enum or fetched from Azure Table Storage for dynamic management?
**Answer:** Store in Azure

## Q10: Should the enhanced AssetManagementView.xaml replace the existing read-only implementation completely or extend it?
**Answer:** Replace