# Merge all wiki markdown files into one big file

$revareWikiDir = "L:\programming\RiderProjects\Revare\Revare-Wiki\docs"
$mieliaWikiDir = "L:\programming\RiderProjects\Revare\Mielia-Wiki\docs"
$rvOutputFile = "L:\programming\RiderProjects\Revare\MERGED_WIKI_RA.md"
$miOutputFile = "L:\programming\RiderProjects\Revare\MERGED_WIKI_MI.md"

# Get all markdown files from wiki directory
$rvMdFiles = Get-ChildItem $revareWikiDir -Filter "*.md" | Sort-Object Name
$miMdFiles = Get-ChildItem $mieliaWikiDir -Filter "*.md" | Sort-Object Name

# RV merging part
# Create/clear output file
"# Revare Wiki - Merged Documentation`n" | Out-File -FilePath $rvOutputFile -Encoding UTF8
"Generated on: $( Get-Date -Format 'yyyy-MM-dd HH:mm:ss' )`n" | Out-File -FilePath $rvOutputFile -Append -Encoding UTF8
"---`n" | Out-File -FilePath $rvOutputFile -Append -Encoding UTF8

# Counter for progress
$count = 0
$total = $rvMdFiles.Count

Write-Host "Merging $total markdown files from wiki directory..." -ForegroundColor Green

foreach ($file in $rvMdFiles)
{
    $count++
    Write-Host "[$count/$total] Processing: $( $file.Name )" -ForegroundColor Cyan

    # Add file separator with filename as heading
    "`n`n" | Out-File -FilePath $rvOutputFile -Append -Encoding UTF8 -NoNewline
    "# ==========================================`n" | Out-File -FilePath $rvOutputFile -Append -Encoding UTF8
    "# FILE: $( $file.Name )`n" | Out-File -FilePath $rvOutputFile -Append -Encoding UTF8
    "# ==========================================`n`n" | Out-File -FilePath $rvOutputFile -Append -Encoding UTF8

    # Read and append file content
    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    $content | Out-File -FilePath $rvOutputFile -Append -Encoding UTF8 -NoNewline

    # Add separator
    "`n`n---`n" | Out-File -FilePath $rvOutputFile -Append -Encoding UTF8
}

Write-Host "`nMerge complete! Output file: $rvOutputFile" -ForegroundColor Green
Write-Host "Total files merged: $total" -ForegroundColor Green

# MI merging part
# Create/clear output file
"# Mielia Wiki - Merged Documentation`n" | Out-File -FilePath $miOutputFile -Encoding UTF8
"Generated on: $( Get-Date -Format 'yyyy-MM-dd HH:mm:ss' )`n" | Out-File -FilePath $miOutputFile -Append -Encoding UTF8
"---`n" | Out-File -FilePath $miOutputFile -Append -Encoding UTF8

# Counter for progress
$count = 0
$total = $miMdFiles.Count

Write-Host "Merging $total markdown files from wiki directory..." -ForegroundColor Green

foreach ($file in $miMdFiles)
{
    $count++
    Write-Host "[$count/$total] Processing: $( $file.Name )" -ForegroundColor Cyan

    # Add file separator with filename as heading
    "`n`n" | Out-File -FilePath $miOutputFile -Append -Encoding UTF8 -NoNewline
    "# ==========================================`n" | Out-File -FilePath $miOutputFile -Append -Encoding UTF8
    "# FILE: $( $file.Name )`n" | Out-File -FilePath $miOutputFile -Append -Encoding UTF8
    "# ==========================================`n`n" | Out-File -FilePath $miOutputFile -Append -Encoding UTF8

    # Read and append file content
    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    $content | Out-File -FilePath $miOutputFile -Append -Encoding UTF8 -NoNewline

    # Add separator
    "`n`n---`n" | Out-File -FilePath $miOutputFile -Append -Encoding UTF8
}

Write-Host "`nMerge complete! Output file: $miOutputFile" -ForegroundColor Green
Write-Host "Total files merged: $total" -ForegroundColor Green
