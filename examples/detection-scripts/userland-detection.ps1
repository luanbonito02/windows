# Userland Detection Script for Intune
# This script checks if BootstrapMate completed successfully (including userland phase)
# Use this for Win32 apps that depend on complete BootstrapMate execution

$regPath = "HKLM:\SOFTWARE\BootstrapMate"
$expectedVersion = "2025.08.30.1300"  # Update this when deploying new versions

Write-Host "Checking BootstrapMate userland completion..."

try {
    # Check for successful completion using LastRunVersion
    $lastRunVersion = Get-ItemProperty -Path $regPath -Name "LastRunVersion" -ErrorAction Stop
    
    if ($lastRunVersion.LastRunVersion -eq $expectedVersion) {
        Write-Host "✅ BootstrapMate completed successfully (including userland)"
        Write-Host "Version: $($lastRunVersion.LastRunVersion)"
        
        # Additional userland-specific verification
        try {
            $userlandStatus = Get-ItemProperty -Path "HKLM:\SOFTWARE\BootstrapMate\Status\Userland" -ErrorAction SilentlyContinue
            if ($userlandStatus) {
                Write-Host "Userland stage: $($userlandStatus.Stage)"
                if ($userlandStatus.CompletionTime) {
                    Write-Host "Userland completion: $($userlandStatus.CompletionTime)"
                }
            }
            
            # Also check setup assistant
            $setupStatus = Get-ItemProperty -Path "HKLM:\SOFTWARE\BootstrapMate\Status\SetupAssistant" -ErrorAction SilentlyContinue
            if ($setupStatus) {
                Write-Host "Setup Assistant stage: $($setupStatus.Stage)"
            }
        }
        catch {
            # Detailed status not available, but LastRunVersion indicates full success
        }
        
        exit 0  # Complete success
    } else {
        Write-Host "⚠️ Version mismatch - found $($lastRunVersion.LastRunVersion), expected $expectedVersion"
        exit 1  # Trigger reinstall
    }
} catch {
    Write-Host "❌ BootstrapMate not completed"
    
    # Check for partial completion or errors
    try {
        $bootstrapMateReg = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
        if ($bootstrapMateReg) {
            if ($bootstrapMateReg.BootstrapStatus) {
                Write-Host "Bootstrap status: $($bootstrapMateReg.BootstrapStatus)"
            }
            if ($bootstrapMateReg.LastError) {
                Write-Host "Last error: $($bootstrapMateReg.LastError)"
            }
            
            # Check individual phase status for more details
            $setupStatus = Get-ItemProperty -Path "HKLM:\SOFTWARE\BootstrapMate\Status\SetupAssistant" -ErrorAction SilentlyContinue
            $userlandStatus = Get-ItemProperty -Path "HKLM:\SOFTWARE\BootstrapMate\Status\Userland" -ErrorAction SilentlyContinue
            
            if ($setupStatus) {
                Write-Host "Setup Assistant: $($setupStatus.Stage)"
            }
            if ($userlandStatus) {
                Write-Host "Userland: $($userlandStatus.Stage)"
            }
        } else {
            Write-Host "No BootstrapMate traces found - never installed"
        }
    }
    catch {
        Write-Host "No BootstrapMate registry found"
    }
    
    exit 1  # Not found or incomplete
}
