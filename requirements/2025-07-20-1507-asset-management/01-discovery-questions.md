# Discovery Questions - Asset Management Tab

## Q1: Should the enhanced Asset Management tab completely replace the existing basic implementation?
**Default if unknown:** Yes (requirements indicate significant feature expansion beyond current read-only interface)

## Q2: For Keyboard/Mouse assets that don't have individual serial numbers, should we create separate asset types with quantity fields?
**Default if unknown:** Yes (mentioned special handling in requirements for "Keyboard – Dell – K1323 – 23" format)

## Q3: Should CSV import validation errors be displayed in a popup dialog as mentioned, or would an inline error panel be acceptable?
**Default if unknown:** Popup dialog (specifically mentioned in requirements)

## Q4: Should the archive view for deleted assets be a separate tab/page or a toggle filter on the main asset list?
**Default if unknown:** Toggle filter (maintains master-detail layout consistency)

## Q5: Should asset serial number uniqueness be enforced globally across all ODS locations or only within each ODS location?
**Default if unknown:** Globally (better data integrity and prevents confusion across locations)