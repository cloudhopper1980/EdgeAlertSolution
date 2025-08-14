# Initial Request - Asset Management Tab

## Original Request
Feature Requirements Document: Asset Management Tab

### 1. Feature Overview
Add a new tab titled 'Asset Management' to the WPF application, providing functionality to manage various non-PC hardware assets via a CRUD interface and optional CSV import. The interface will use a master-detail layout to allow efficient viewing and editing.

### 2. Navigation & Access
- Tab Name: Asset Management
- Visibility: No restrictions; available to all users with existing asset permissions
- Layout: Master-detail
- Master list: All assets with filter/sort capabilities
- Detail panel: Editable form with CRUD actions

### 3. Supported Asset Types
Initial supported asset types:
- Monitor
- Printer
- Label Printer
- Scanner
- Docking Station
- External Hard Drive
- Network Switch
- Router
- UPS (Uninterruptible Power Supply)
- Webcam
- Keyboard – Can we do something different for these in the table can we have this so they can just add Make Model and then numbers e.g Keyboard – Dell – K1323 – 23 – Mouse Dell - K11 -12 – Hopefully that makes sense as they don't really have serial numbers
- Mouse as above

Note: PC and Laptop assets are managed separately and are excluded from this module.

### 4. Common Fields
| Field Name | Data Type | Notes |
|------------|-----------|-------|
| Asset ID | GUID/String | Auto-generated |
| Asset Type | Dropdown | From fixed list |
| Manufacturer | String | Optional |
| Model | String | Optional |
| Serial Number | String | Required, unique |
| Purchase Date | Date | Optional |
| Warranty Expiry Date | Date | Optional |
| Assigned To | String | Optional |
| Asset Location | String | Optional |
| ODS Location | Dropdown | Required, from existing ODS list |
| Status | Enum/String | Active / Inactive / Archived |
| Notes | String | Optional |
| Created At | DateTime | Auto-populated |
| Updated At | DateTime | Auto-updated on change |
| Deleted At | DateTime | Set on soft delete |

### 5. CSV Upload
- Format: Uses the above fields as column headers
- Template Download: Available to users
- Validation:
  - Required fields enforced
  - Unique serials checked
  - Invalid data triggers a popup-style error message
- Import Scope: Only non-PC/laptop assets
- ODS: Must match dropdown options exactly

### 6. CRUD Capabilities
- Full Create, Read, Update, Delete support
- Soft Delete Only (for this module)
- Records flagged as deleted with `Deleted At` timestamp
- Recoverable up to 1 month
- Recovered assets are restored to their prior status
- Archive View: Separate archive view for deleted assets
- Recovery: Via "Recover" button in archive

### 7. Data Storage
- Stored in Azure Table Storage
- New dedicated table to isolate this module
- ODS Location included as a property; validated against existing list

### 8. Filtering, Sorting, and Searching
- Advanced filters by field (e.g., Asset Type, Status, ODS)
- Sorting on all columns
- Pagination or lazy loading for performance
- Can we group all asset types as standard e.g Monitors, printers, etc.

### 9. UI/UX Expectations
- Modal forms or master-detail reuse for editing
- Dropdown for Asset Type and ODS
- All other fields use appropriate controls (free text, date picker, etc.)
- No image or document attachments initially
- Export, search, and import behaviors will match existing asset modules