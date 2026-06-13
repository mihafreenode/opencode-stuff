$glyphLine = 'UTF8: ' + [string]::Concat([char]0x2713,' ',[char]0x03BB,' ',[char]0x20AC,' ',[char]0x2014,' ',[char]0x2022,' ',[char]0x2502,' ',[char]0x2500,' ',[char]0xE0B6,' ',[char]0xE0B4)
Write-Host 'CASE1'
Write-Host "TERM=$env:TERM"
Write-Host "LANG=$env:LANG"
Write-Host "LC_ALL=$env:LC_ALL"
Write-Host $glyphLine
