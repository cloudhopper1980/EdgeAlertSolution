# Define the source folder and the output file
$sourceFolder = "D:\Repos\Repo_Next\EdgeAlert\EdgeAlertSignalClient"
$outputFile = "$([Environment]::GetFolderPath('Desktop'))\Export EdgeAlert\concatenated_files.txt"

# Define the subfolders to skip (relative paths from the source folder)
$skipFolders = @(
    "bin"
)

# Ensure the output directory exists
$outputDir = [System.IO.Path]::GetDirectoryName($outputFile)
if (-not (Test-Path -Path $outputDir)) {
    New-Item -ItemType Directory -Path $outputDir -Force
}

# Clear the output file if it already exists
if (Test-Path -Path $outputFile) {
    Remove-Item -Path $outputFile -Force
}

# Traverse all .cs and .xaml files in the source folder and its subfolders
Get-ChildItem -Path $sourceFolder -Recurse -Include *.cs, *.xaml | ForEach-Object {
    $filePath = $_.FullName
    $relativePath = $filePath.Substring($sourceFolder.Length + 1)
    $folderPath = [System.IO.Path]::GetDirectoryName($relativePath)
    $folderName = $folderPath -replace '\\', '_'
    $folderOutputFile = "$([Environment]::GetFolderPath('Desktop'))\Export EdgeAlert\$folderName.txt"

    # Check if the current file path contains any of the skip folders
    $skip = $false
    foreach ($skipFolder in $skipFolders) {
        if ($filePath -like "*\$skipFolder\*") {
            $skip = $true
            break
        }
    }

    if (-not $skip) {
        # Add a heading with the file name to the concatenated file
        Add-Content -Path $outputFile -Value "Filename: $filePath"
        Add-Content -Path $outputFile -Value "------------------------"
        # Add the content of the file to the concatenated file
        Get-Content -Path $filePath | Add-Content -Path $outputFile
        Add-Content -Path $outputFile -Value ""
        Add-Content -Path $outputFile -Value ""

        # Add a heading with the file name to the folder-specific file
        Add-Content -Path $folderOutputFile -Value "Filename: $filePath"
        Add-Content -Path $folderOutputFile -Value "------------------------"
        # Add the content of the file to the folder-specific file
        Get-Content -Path $filePath | Add-Content -Path $folderOutputFile
        Add-Content -Path $folderOutputFile -Value ""
        Add-Content -Path $folderOutputFile -Value ""
    }
}

Write-Output "All files have been concatenated into $outputFile"
Write-Output "Separate files have been created for each folder in the Export EdgeAlert directory on your Desktop"
