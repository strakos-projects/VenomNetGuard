# Check for Administrator privileges
if (!([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "[-] ERROR: This script must be run as Administrator." -ForegroundColor Red
    exit
}

# 1. Define base test folder with a random suffix
$randomSuffix = -join ((65..90) + (97..122) | Get-Random -Count 6 | ForEach-Object {[char]$_})
$testVault = "C:\VenomGuard_TestVault_$randomSuffix"

Write-Host "--- VENOM NETGUARD: ADVANCED CFA SHIELD TEST ---" -ForegroundColor Cyan
Write-Host "[*] Creating random test vault: $testVault" -ForegroundColor Gray

# 2. Remove protection if exists (for testing)
Remove-MpPreference -ControlledFolderAccessProtectedFolders $testVault -ErrorAction SilentlyContinue

# 3. Create main test vault folder
if (!(Test-Path $testVault)) {
    New-Item -Path $testVault -ItemType Directory -Force | Out-Null
}

# 4. Create "hidden" subfolders
$hiddenFolders = @("SecretDocs", "TempCache", "SysData")
foreach ($folder in $hiddenFolders) {
    $fullPath = Join-Path $testVault $folder
    if (!(Test-Path $fullPath)) {
        New-Item -Path $fullPath -ItemType Directory -Force | Out-Null
        # Set hidden attribute
        (Get-Item $fullPath).Attributes += 'Hidden'
    }
}

# 5. Create dummy files in hidden subfolders
foreach ($folder in $hiddenFolders) {
    $filePath = Join-Path $testVault "$folder\dummy_$(Get-Random -Minimum 1000 -Maximum 9999).txt"
    "Test content for $folder" | Out-File -FilePath $filePath -Force -Encoding utf8
}

# 6. Lock folder via Windows Defender Controlled Folder Access
Write-Host "[*] Locking vault via Windows Defender CFA..." -ForegroundColor Gray
Add-MpPreference -ControlledFolderAccessProtectedFolders $testVault -ErrorAction SilentlyContinue
Start-Sleep -Seconds 2

# 7. TEST 1: Simulate malicious write in hidden folder
$attackFile = Join-Path $testVault "SecretDocs\malware_sim_$(Get-Date -Format 'HHmmss_fff').exe"
Write-Host "`n[TEST 1] Simulating malicious write: $attackFile"
try {
    "MZP_FakePayload" | Out-File -FilePath $attackFile -ErrorAction Stop
    Write-Host " [-] FAILURE: File was created. CFA is not blocking this path!" -ForegroundColor Red
} catch {
    if ($_.Exception.Message -match "access to the path" -or $_.Exception -is [System.UnauthorizedAccessException]) {
        Write-Host " [+] SUCCESS: Defender blocked the write." -ForegroundColor Green
    } else {
        Write-Host " [!] UNEXPECTED ERROR: $($_.Exception.Message)" -ForegroundColor Yellow
    }
}

# 8. TEST 2: Simulate deletion attempt
$dummyFile = Join-Path $testVault "SecretDocs\dummy_1234.txt"
Write-Host "`n[TEST 2] Simulating deletion: $dummyFile"
if (Test-Path $dummyFile) {
    try {
        Remove-Item -Path $dummyFile -Force -ErrorAction Stop
        Write-Host " [-] FAILURE: File was deleted! Protection failed." -ForegroundColor Red
    } catch {
        Write-Host " [+] SUCCESS: Deletion blocked." -ForegroundColor Green
    }
} else {
    Write-Host " [!] Dummy file does not exist. Test skipped." -ForegroundColor Yellow
}

Write-Host "`n[*] Diagnostics complete." -ForegroundColor Cyan