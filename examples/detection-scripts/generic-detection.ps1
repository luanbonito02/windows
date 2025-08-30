# BootstrapMate Detection Script - LastRunVersion Method
# This script checks for successful BootstrapMate completion using the LastRunVersion registry key
# Suitable for Intune Win32 app detection

param(
    [string]$ExpectedVersion = "2025.08.30.1300"  # Update this when deploying new versions
)

$regPath = "HKLM:\SOFTWARE\BootstrapMate"

Write-Host "Checking BootstrapMate completion status..."
Write-Host "Expected version: $ExpectedVersion"

try {
    # Check for the LastRunVersion registry key
    $lastRunVersion = Get-ItemProperty -Path $regPath -Name "LastRunVersion" -ErrorAction Stop
    
    if ($lastRunVersion.LastRunVersion -eq $ExpectedVersion) {
        Write-Host "✅ BootstrapMate $ExpectedVersion completed successfully"
        
        # Optional: Show additional diagnostic information
        try {
            $allProperties = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
            if ($allProperties.CompletionTime) {
                Write-Host "Completion time: $($allProperties.CompletionTime)"
            }
            if ($allProperties.BootstrapStatus) {
                Write-Host "Bootstrap status: $($allProperties.BootstrapStatus)"
            }
            if ($allProperties.PackageArchitecture) {
                Write-Host "Package architecture: $($allProperties.PackageArchitecture)"
            }
        }
        catch {
            # Additional properties not available, continue
        }
        
        exit 0  # Found - app is installed
    } else {
        Write-Host "⚠️ Found version $($lastRunVersion.LastRunVersion), expected $ExpectedVersion"
        Write-Host "This will trigger a reinstall to update to the expected version"
        exit 1  # Wrong version - trigger reinstall
    }
} catch {
    Write-Host "❌ BootstrapMate not found or never completed successfully"
    Write-Host "Registry key not found: $regPath\LastRunVersion"
    
    # Optional: Check for any BootstrapMate traces for troubleshooting
    try {
        $anyBootstrapMateKey = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
        if ($anyBootstrapMateKey) {
            Write-Host "BootstrapMate registry key exists but LastRunVersion is missing"
            if ($anyBootstrapMateKey.BootstrapStatus) {
                Write-Host "Bootstrap status: $($anyBootstrapMateKey.BootstrapStatus)"
            }
            if ($anyBootstrapMateKey.LastError) {
                Write-Host "Last error: $($anyBootstrapMateKey.LastError)"
            }
        } else {
            Write-Host "No BootstrapMate registry traces found"
        }
    }
    catch {
        Write-Host "No BootstrapMate registry key found at all"
    }
    
    exit 1  # Not found - trigger install
}
