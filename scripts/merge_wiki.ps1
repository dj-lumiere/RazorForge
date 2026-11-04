# Merge all wiki markdown files into one big file

$revareWikiDir = "L:\programming\RiderProjects\Revare\Revare-Wiki\docs"
$mieliaWikiDir = "L:\programming\RiderProjects\Revare\Mielia-Wiki\docs"
$raOutputFile = "L:\programming\RiderProjects\Revare\MERGED_WIKI_RA.md"
$miOutputFile = "L:\programming\RiderProjects\Revare\MERGED_WIKI_MI.md"

# Get all markdown files from wiki directory
$raMdFiles = Get-ChildItem $revareWikiDir -Filter "*.md" | Sort-Object Name
$miMdFiles = Get-ChildItem $mieliaWikiDir -Filter "*.md" | Sort-Object Name

# RA merging part
# Create/clear output file
"# Revare Wiki - Merged Documentation`n" | Out-File -FilePath $raOutputFile -Encoding UTF8
"Generated on: $( Get-Date -Format 'yyyy-MM-dd HH:mm:ss' )`n" | Out-File -FilePath $raOutputFile -Append -Encoding UTF8
"---`n" | Out-File -FilePath $raOutputFile -Append -Encoding UTF8

# Counter for progress
$count = 0
$total = $raMdFiles.Count

Write-Host "Merging $total markdown files from wiki directory..." -ForegroundColor Green

foreach ($file in $raMdFiles)
{
    $count++
    Write-Host "[$count/$total] Processing: $( $file.Name )" -ForegroundColor Cyan

    # Add file separator with filename as heading
    "`n`n" | Out-File -FilePath $raOutputFile -Append -Encoding UTF8 -NoNewline
    "# ==========================================`n" | Out-File -FilePath $raOutputFile -Append -Encoding UTF8
    "# FILE: $( $file.Name )`n" | Out-File -FilePath $raOutputFile -Append -Encoding UTF8
    "# ==========================================`n`n" | Out-File -FilePath $raOutputFile -Append -Encoding UTF8

    # Read and append file content
    $content = Get-Content -Path $file.FullName -Raw -Encoding UTF8
    $content | Out-File -FilePath $raOutputFile -Append -Encoding UTF8 -NoNewline

    # Add separator
    "`n`n---`n" | Out-File -FilePath $raOutputFile -Append -Encoding UTF8
}

Write-Host "`nMerge complete! Output file: $raOutputFile" -ForegroundColor Green
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
