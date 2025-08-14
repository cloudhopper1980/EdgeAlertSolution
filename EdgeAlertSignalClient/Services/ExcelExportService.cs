using ClosedXML.Excel;
using EdgeAlertSignalClient.Models;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace EdgeAlertSignalClient.Services
{
    /// <summary>
    /// Service responsible for generating Excel exports.
    /// </summary>
    public class ExcelExportService
    {
        private readonly ILogger<ExcelExportService> _logger;

        public ExcelExportService(ILogger<ExcelExportService> logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Creates an Excel workbook with "Summary" and "Details" sheets based on provided data.
        /// Populates both sheets with data and formatting.
        /// </summary>
        /// <param name="summaryData">Collection of filtered summarized report data.</param>
        /// <param name="detailedData">Collection of filtered and sorted detailed report data.</param>
        /// <param name="filePath">The full path where the Excel file will be saved.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        public async Task ExportTimeLogDataAsync(
            IEnumerable<SummarizedReportData> summaryData,
            IEnumerable<DetailedReportData> detailedData, // Now used
            string filePath)
        {
            await Task.Run(() => // Run Excel operations on background thread
            {
                _logger?.LogInformation("Starting Excel file generation for: {FilePath}", filePath);
                try
                {
                    using (var workbook = new XLWorkbook())
                    {
                        // --- Populate Summary Sheet (from Phase 2) ---
                        PopulateSummarySheet(workbook, summaryData);

                        // --- Populate Details Sheet (Phase 3 Implementation) ---
                        PopulateDetailsSheet(workbook, detailedData);

                        // Save the workbook
                        workbook.SaveAs(filePath);
                        _logger?.LogInformation("Excel file saved successfully with Summary and Details data at {FilePath}", filePath);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "Error generating Excel file at {FilePath}", filePath);
                    throw; // Re-throw for the command to handle
                }
            });
        }

        /// <summary>
        /// Populates the "Summary" worksheet with the provided data and formatting.
        /// </summary>
        private void PopulateSummarySheet(XLWorkbook workbook, IEnumerable<SummarizedReportData> summaryData)
        {
            _logger?.LogDebug("Populating Summary sheet...");
            var sheet = workbook.Worksheets.Add("Summary");

            // Define Headers
            string[] headers = { "User ID", "Date", "ODS Practice", "Active Time", "Away Time", "Offline Time" };
            for (int i = 0; i < headers.Length; i++)
            {
                sheet.Cell(1, i + 1).Value = headers[i];
            }
            sheet.Row(1).Style.Font.Bold = true;

            // Write Data Rows
            int currentRow = 2;
            if (summaryData != null && summaryData.Any())
            {
                foreach (var dataRow in summaryData)
                {
                    sheet.Cell(currentRow, 1).Value = dataRow.UserID;
                    sheet.Cell(currentRow, 2).Value = dataRow.Date;
                    sheet.Cell(currentRow, 2).Style.DateFormat.Format = "yyyy-MM-dd";
                    sheet.Cell(currentRow, 3).Value = dataRow.ODS;

                    SetTimeCellValue(sheet.Cell(currentRow, 4), dataRow.TotalActiveSeconds);
                    SetTimeCellValue(sheet.Cell(currentRow, 5), dataRow.TotalAwaySeconds);
                    SetTimeCellValue(sheet.Cell(currentRow, 6), dataRow.TotalOfflineSeconds);

                    currentRow++;
                }
                _logger?.LogDebug("Populated {Count} data rows in Summary sheet.", summaryData.Count());
            }
            else
            {
                sheet.Cell(currentRow, 1).Value = "No summary data available for the selected filters.";
                sheet.Range(currentRow, 1, currentRow, headers.Length).Merge();
                _logger?.LogWarning("No summary data provided to PopulateSummarySheet.");
            }

            sheet.Columns().AdjustToContents();
            _logger?.LogDebug("Applied auto-width to Summary sheet columns.");
        }

        /// <summary>
        /// Populates the "Details" worksheet with the provided data and formatting.
        /// </summary>
        private void PopulateDetailsSheet(XLWorkbook workbook, IEnumerable<DetailedReportData> detailedData)
        {
            _logger?.LogDebug("Populating Details sheet...");
            // Add or get the sheet
            var sheet = workbook.Worksheets.Contains("Details") ? workbook.Worksheet("Details") : workbook.Worksheets.Add("Details");

            // Define Headers
            string[] headers = { "User Name", "Machine ID", "State", "Start Time (Local)", "Duration", "ODS", "Context" };
            for (int i = 0; i < headers.Length; i++)
            {
                sheet.Cell(1, i + 1).Value = headers[i];
            }
            sheet.Row(1).Style.Font.Bold = true;

            // Write Data Rows
            int currentRow = 2;
            if (detailedData != null && detailedData.Any())
            {
                foreach (var dataRow in detailedData) // Data should already be sorted
                {
                    sheet.Cell(currentRow, 1).Value = dataRow.UserName;
                    sheet.Cell(currentRow, 2).Value = dataRow.MachineID;
                    sheet.Cell(currentRow, 3).Value = dataRow.State;

                    // Convert Start Time UTC to Local and format
                    try
                    {
                        DateTime localStartTime = TimeZoneInfo.ConvertTimeFromUtc(dataRow.StateStartTime, TimeZoneInfo.Local);
                        sheet.Cell(currentRow, 4).Value = localStartTime;
                        sheet.Cell(currentRow, 4).Style.DateFormat.Format = "yyyy-MM-dd HH:mm:ss";
                    }
                    catch (ArgumentException argEx) // Handle potential invalid DateTimeKind or TimeZone issues
                    {
                        _logger?.LogWarning(argEx, "Could not convert StateStartTime {UtcTime} to local time for User {User}. Writing as string.", dataRow.StateStartTime, dataRow.UserName);
                        sheet.Cell(currentRow, 4).Value = $"{dataRow.StateStartTime:yyyy-MM-dd HH:mm:ss} (UTC?)"; // Fallback
                    }


                    // Format Duration using helper (handles nullable double)
                    SetTimeCellValue(sheet.Cell(currentRow, 5), dataRow.CalculatedDurationSeconds ?? 0); // Pass 0 if null

                    sheet.Cell(currentRow, 6).Value = dataRow.ODS;
                    sheet.Cell(currentRow, 7).Value = dataRow.Context;

                    currentRow++;
                }
                _logger?.LogDebug("Populated {Count} data rows in Details sheet.", detailedData.Count());
            }
            else
            {
                // Clear potential placeholder header from Phase 2
                sheet.Cell(1, 1).Value = headers[0];
                // Add message if no data
                sheet.Cell(currentRow, 1).Value = "No detailed data available for the selected filters.";
                sheet.Range(currentRow, 1, currentRow, headers.Length).Merge();
                _logger?.LogWarning("No detailed data provided to PopulateDetailsSheet.");
            }

            // Apply Auto Width
            sheet.Columns().AdjustToContents();
            _logger?.LogDebug("Applied auto-width to Details sheet columns.");
        }

        /// <summary>
        /// Helper method to set cell value and format for time durations.
        /// Stores duration as fraction of a day for Excel calculations and applies [h]:mm:ss format.
        /// </summary>
        private void SetTimeCellValue(IXLCell cell, double totalSeconds)
        {
            if (totalSeconds < 0) totalSeconds = 0;
            cell.Value = TimeSpan.FromSeconds(totalSeconds).TotalDays;
            cell.Style.NumberFormat.Format = "[h]:mm:ss";
        }
    }
}