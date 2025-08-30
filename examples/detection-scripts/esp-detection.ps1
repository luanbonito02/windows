# ESP (Setup Assistant) Detection Script for Intune
# This script checks if BootstrapMate completed successfully using the LastRunVersion method
# Use this for your ESP/pre-login BootstrapMate package

$regPath = "HKLM:\SOFTWARE\BootstrapMate"
$expectedVersion = "2025.08.30.1300"  # Update this when deploying new versions

Write-Host "Checking BootstrapMate ESP completion..."

try {
    # Check for successful completion using LastRunVersion
    $lastRunVersion = Get-ItemProperty -Path $regPath -Name "LastRunVersion" -ErrorAction Stop
    
    if ($lastRunVersion.LastRunVersion -eq $expectedVersion) {
        Write-Host "✅ BootstrapMate ESP completed successfully"
        Write-Host "Version: $($lastRunVersion.LastRunVersion)"
        
        # Additional ESP-specific verification
        try {
            $setupAssistantStatus = Get-ItemProperty -Path "HKLM:\SOFTWARE\BootstrapMate\Status\SetupAssistant" -ErrorAction SilentlyContinue
            if ($setupAssistantStatus) {
                Write-Host "Setup Assistant stage: $($setupAssistantStatus.Stage)"
                if ($setupAssistantStatus.CompletionTime) {
                    Write-Host "Setup Assistant completion: $($setupAssistantStatus.CompletionTime)"
                }
            }
        }
        catch {
            # Detailed status not available, but LastRunVersion indicates success
        }
        
        exit 0  # ESP completed successfully
    } else {
        Write-Host "⚠️ Version mismatch - found $($lastRunVersion.LastRunVersion), expected $expectedVersion"
        exit 1  # Trigger reinstall
    }
} catch {
    Write-Host "❌ BootstrapMate ESP not completed"
    
    # Check for partial installation or errors
    try {
        $bootstrapMateReg = Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue
        if ($bootstrapMateReg) {
            if ($bootstrapMateReg.BootstrapStatus) {
                Write-Host "Bootstrap status: $($bootstrapMateReg.BootstrapStatus)"
            }
            if ($bootstrapMateReg.LastError) {
                Write-Host "Last error: $($bootstrapMateReg.LastError)"
            }
            if ($bootstrapMateReg.InstallationStarted) {
                Write-Host "Installation started: $($bootstrapMateReg.InstallationStarted)"
            }
        } else {
            Write-Host "No BootstrapMate traces found - never installed"
        }
    }
    catch {
        Write-Host "No BootstrapMate registry found"
    }
    
    exit 1  # Not found or failed
}
