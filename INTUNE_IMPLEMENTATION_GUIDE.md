# BootstrapMate Intune Implementation Guide

This guide provides complete instructions for implementing BootstrapMate in Microsoft Intune for automated Windows device provisioning during OOBE/Autopilot scenarios.

## Overview

BootstrapMate is a lightweight bootstrapping tool that downloads and installs packages during Windows OOBE/ESP or after user login. It provides reliable registry-based detection for Intune and supports multiple package formats.

## Key Features

- **Dual Phase Support**: Setup Assistant (pre-login/ESP) and Userland (post-login)
- **Package Types**: MSI, EXE, PowerShell scripts, Chocolatey packages (.nupkg)
- **Registry Detection**: Simple, reliable detection for Intune Win32 apps
- **Architecture Support**: x64 and ARM64 with conditional installation
- **Admin Escalation**: Automatic privilege elevation when required

## Registry Detection Contract

BootstrapMate writes completion status to the registry **only after successful completion**:

```
HKLM\SOFTWARE\BootstrapMate\
└── LastRunVersion = "2025.08.30.1300"    ← Primary detection key for Intune
```

### Detection Script for Intune Win32 App

Use this PowerShell detection script in your Intune Win32 app configuration:

```powershell
# Intune Detection Script for BootstrapMate
$regPath = "HKLM:\SOFTWARE\BootstrapMate"
$expectedVersion = "2025.08.30.1300"  # Update this when you deploy new versions

try {
    $lastRunVersion = Get-ItemProperty -Path $regPath -Name "LastRunVersion" -ErrorAction Stop
    if ($lastRunVersion.LastRunVersion -eq $expectedVersion) {
        Write-Output "BootstrapMate $expectedVersion completed successfully"
        exit 0  # Found - app is installed
    } else {
        Write-Output "Found version $($lastRunVersion.LastRunVersion), expected $expectedVersion"
        exit 1  # Wrong version - trigger reinstall
    }
} catch {
    Write-Output "BootstrapMate not found or never completed successfully"
    exit 1  # Not found - trigger install
}
```

## Intune Win32 App Configuration

### 1. Package Preparation

Create your Win32 app package with these files:

```
BootstrapMate-Package/
├── installapplications.exe         # BootstrapMate executable (x64 or ARM64)
├── appsettings.json                # Configuration file (optional)
├── install.ps1                     # Installation script
└── detection.ps1                   # Detection script (above)
```

### 2. Installation Script Template

Create `install.ps1` with architecture detection and bootstrap execution:

```powershell
# BootstrapMate OOBE Installation Script
# This script runs during Windows OOBE to install the BootstrapMate bootstrap tool

[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

Write-Host "=================================="
Write-Host "BootstrapMate OOBE Bootstrap" 
Write-Host "=================================="
Write-Host "Installing BootstrapMate during OOBE..."

# Architecture guard - ensure this script only runs on compatible systems
$systemArchitecture = (Get-WmiObject -Class Win32_Processor | Select-Object -First 1 -ExpandProperty Architecture)
$processorName = (Get-WmiObject -Class Win32_Processor | Select-Object -First 1 -ExpandProperty Name)

Write-Host "System Architecture Detection:" -ForegroundColor Cyan
Write-Host "  Processor: $processorName" -ForegroundColor Gray
Write-Host "  Architecture Code: $systemArchitecture" -ForegroundColor Gray

# Architecture codes: 0=x86, 1=MIPS, 2=Alpha, 3=PowerPC, 5=ARM, 6=Itanium, 9=x64, 12=ARM64
# Update this based on your package architecture
$expectedArchitecture = 9  # 9 = x64, 12 = ARM64
$isCorrectSystem = ($systemArchitecture -eq $expectedArchitecture)

if (-not $isCorrectSystem) {
    $architectureName = switch ($systemArchitecture) {
        0 { "x86 (32-bit)" }
        5 { "ARM (32-bit)" }
        9 { "x64" }
        12 { "ARM64" }
        default { "Unknown ($systemArchitecture)" }
    }
    
    $expectedName = switch ($expectedArchitecture) {
        9 { "x64" }
        12 { "ARM64" }
        default { "Unknown" }
    }
    
    Write-Host "❌ ARCHITECTURE MISMATCH!" -ForegroundColor Red
    Write-Host "This is the $expectedName BootstrapMate package, but the system is: $architectureName" -ForegroundColor Red
    Write-Host "The deployment will exit to prevent installing incompatible binaries." -ForegroundColor Red
    
    # Set registry key to indicate architecture mismatch
    $regPath = "HKLM:\SOFTWARE\BootstrapMate"
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }
    Set-ItemProperty -Path $regPath -Name "BootstrapStatus" -Value "ArchitectureMismatch"
    Set-ItemProperty -Path $regPath -Name "ExpectedArchitecture" -Value $expectedName
    Set-ItemProperty -Path $regPath -Name "DetectedArchitecture" -Value $architectureName
    Set-ItemProperty -Path $regPath -Name "ErrorTime" -Value (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    
    throw "Architecture mismatch: This $expectedName package cannot run on $architectureName system"
}

Write-Host "✅ Architecture verification passed" -ForegroundColor Green

try {
    # Create BootstrapMate directory
    $installAppsDir = "$env:ProgramFiles\BootstrapMate"
    
    if (-not (Test-Path $installAppsDir)) {
        New-Item -ItemType Directory -Path $installAppsDir -Force | Out-Null
        Write-Host "Created directory: $installAppsDir"
    }
    
    # Copy BootstrapMate executable and configuration
    Write-Host "Copying BootstrapMate files..."
    
    $sourceFiles = @(
        "installapplications.exe",
        "appsettings.json"
    )
    
    foreach ($file in $sourceFiles) {
        if (Test-Path $file) {
            Copy-Item $file "$installAppsDir\$file" -Force
            Write-Host "Copied: $file"
        } else {
            Write-Warning "Source file not found: $file"
        }
    }
    
    # Set registry key to indicate BootstrapMate installation started
    Write-Host "Setting BootstrapMate installation registry key..."
    $regPath = "HKLM:\SOFTWARE\BootstrapMate"
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }
    Set-ItemProperty -Path $regPath -Name "InstallationStarted" -Value (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    Set-ItemProperty -Path $regPath -Name "BootstrapPhase" -Value "OOBE"
    Set-ItemProperty -Path $regPath -Name "InstallPath" -Value $installAppsDir
    Set-ItemProperty -Path $regPath -Name "PackageArchitecture" -Value $expectedName
    Set-ItemProperty -Path $regPath -Name "SystemArchitecture" -Value $systemArchitecture
    Set-ItemProperty -Path $regPath -Name "ProcessorName" -Value $processorName

    # Run BootstrapMate with your bootstrap manifest URL
    Write-Host "Launching BootstrapMate bootstrap process..." -ForegroundColor Green
    $bootstrapUrl = "https://your-domain.com/bootstrap/installapplications.json"  # UPDATE THIS URL
    
    $installAppsExe = Join-Path $installAppsDir "installapplications.exe"
    if (Test-Path $installAppsExe) {
        Write-Host "Running: $installAppsExe --url `"$bootstrapUrl`"" -ForegroundColor Cyan
        
        # Check executable properties first
        try {
            $fileInfo = Get-Item $installAppsExe
            Write-Host "Executable size: $([math]::Round($fileInfo.Length / 1MB, 2)) MB" -ForegroundColor Gray
            
            $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($installAppsExe)
            Write-Host "File version: $($versionInfo.FileVersion)" -ForegroundColor Gray
            Write-Host "Product name: $($versionInfo.ProductName)" -ForegroundColor Gray
            
            $signature = Get-AuthenticodeSignature -FilePath $installAppsExe
            Write-Host "Digital signature status: $($signature.Status)" -ForegroundColor $(if($signature.Status -eq "Valid") { "Green" } else { "Yellow" })
            if ($signature.SignerCertificate) {
                Write-Host "Signed by: $($signature.SignerCertificate.Subject)" -ForegroundColor Gray
            }
        } catch {
            Write-Host "Warning: Could not get executable properties: $($_.Exception.Message)" -ForegroundColor Yellow
        }
        
        # Execute BootstrapMate with the manifest URL
        Write-Host "Starting BootstrapMate bootstrap..." -ForegroundColor Green
        $process = Start-Process -FilePath $installAppsExe -ArgumentList "--url", "`"$bootstrapUrl`"" -Wait -PassThru -NoNewWindow
        
        if ($process.ExitCode -eq 0) {
            Write-Host "BootstrapMate bootstrap completed successfully!" -ForegroundColor Green
            
            # Verify the registry key was written
            $lastRunVersion = Get-ItemProperty -Path $regPath -Name "LastRunVersion" -ErrorAction SilentlyContinue
            if ($lastRunVersion) {
                Write-Host "Success: LastRunVersion registry key set to: $($lastRunVersion.LastRunVersion)" -ForegroundColor Green
                Set-ItemProperty -Path $regPath -Name "BootstrapStatus" -Value "Success"
            } else {
                Write-Host "Warning: LastRunVersion registry key not found after successful run" -ForegroundColor Yellow
                Set-ItemProperty -Path $regPath -Name "BootstrapStatus" -Value "SuccessNoRegistry"
            }
            Set-ItemProperty -Path $regPath -Name "CompletionTime" -Value (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
        } else {
            Write-Host "BootstrapMate bootstrap failed with exit code: $($process.ExitCode)" -ForegroundColor Red
            Set-ItemProperty -Path $regPath -Name "BootstrapStatus" -Value "Failed"
            Set-ItemProperty -Path $regPath -Name "LastError" -Value "Exit code: $($process.ExitCode)"
            Set-ItemProperty -Path $regPath -Name "ErrorTime" -Value (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
            
            throw "BootstrapMate failed with exit code: $($process.ExitCode)"
        }
    } else {
        Write-Error "BootstrapMate executable not found at: $installAppsExe"
    }

} catch {
    Write-Host "Error during BootstrapMate installation: $($_.Exception.Message)" -ForegroundColor Red
    
    # Set error registry key for troubleshooting
    $regPath = "HKLM:\SOFTWARE\BootstrapMate"
    if (-not (Test-Path $regPath)) {
        New-Item -Path $regPath -Force | Out-Null
    }
    Set-ItemProperty -Path $regPath -Name "BootstrapStatus" -Value "Error"
    Set-ItemProperty -Path $regPath -Name "LastError" -Value $_.Exception.Message
    Set-ItemProperty -Path $regPath -Name "ErrorTime" -Value (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    
    throw
}

Write-Host "BootstrapMate installation completed." -ForegroundColor Green
```

### 3. Intune Win32 App Settings

Configure your Intune Win32 app with these settings:

#### Basic Information
- **Name**: BootstrapMate OOBE Bootstrap
- **Description**: Automated software provisioning during Windows OOBE
- **Publisher**: Your Organization
- **Category**: Computer Management

#### Program Settings
- **Install command**: `powershell.exe -ExecutionPolicy Bypass -File install.ps1`
- **Uninstall command**: `powershell.exe -ExecutionPolicy Bypass -Command "Remove-Item -Path 'HKLM:\SOFTWARE\BootstrapMate' -Recurse -Force -ErrorAction SilentlyContinue; Remove-Item -Path '$env:ProgramFiles\BootstrapMate' -Recurse -Force -ErrorAction SilentlyContinue"`
- **Install behavior**: System
- **Device restart behavior**: No specific action

#### Requirements
- **Operating system architecture**: 64-bit (or configure separate packages for x64/ARM64)
- **Minimum operating system**: Windows 10 1903
- **Disk space required**: 100 MB
- **Physical memory required**: 512 MB

#### Detection Rules
- **Rules format**: Use custom detection script
- **Script file**: Upload the detection.ps1 script from above

#### Dependencies
- None (BootstrapMate is self-contained)

#### Supersedence
- Configure supersedence when deploying newer versions

## Bootstrap Manifest Configuration

Create your bootstrap manifest at the URL specified in your installation script:

```json
{
  "setupassistant": [
    {
      "name": "Company Authentication Package",
      "type": "nupkg",
      "url": "https://your-domain.com/packages/auth-package.nupkg",
      "file": "auth-package.nupkg"
    },
    {
      "name": "Company Tools (x64)",
      "type": "nupkg", 
      "url": "https://your-domain.com/packages/tools-x64.nupkg",
      "file": "tools-x64.nupkg",
      "condition": "architecture_x64"
    },
    {
      "name": "Company Tools (ARM64)",
      "type": "nupkg",
      "url": "https://your-domain.com/packages/tools-arm64.nupkg", 
      "file": "tools-arm64.nupkg",
      "condition": "architecture_arm64"
    }
  ],
  "userland": [
    {
      "name": "User Software Package",
      "type": "msi",
      "url": "https://your-domain.com/packages/user-software.msi",
      "file": "user-software.msi",
      "arguments": "/quiet ALLUSERS=0"
    }
  ]
}
```

## Deployment Strategy

### 1. Autopilot Deployment

1. **Create Win32 App**: Package BootstrapMate as described above
2. **Assign to Device Groups**: Target your Autopilot device groups
3. **Set as Required**: Deploy as required during ESP
4. **Configure Dependencies**: Ensure this runs before other software

### 2. Group Assignments

- **Target**: Device groups (Autopilot devices)
- **Assignment type**: Required
- **Delivery optimization**: Download content in background using HTTP only

### 3. ESP Configuration

In your Autopilot profile ESP settings:
- **Show app installation progress**: Yes
- **Block device use until required apps install**: Yes
- **Include BootstrapMate in required apps list**

## Troubleshooting

### Registry Diagnostic Keys

BootstrapMate creates additional registry keys for troubleshooting:

```
HKLM\SOFTWARE\BootstrapMate\
├── LastRunVersion              # Only exists after successful completion
├── BootstrapStatus            # InstallationStarted, Success, Failed, Error, ArchitectureMismatch
├── InstallationStarted        # Timestamp when installation began
├── CompletionTime            # Timestamp when bootstrap completed
├── LastError                 # Error message if failed
├── ErrorTime                 # Timestamp of last error
├── InstallPath               # Where BootstrapMate was installed
├── PackageArchitecture       # Architecture of deployed package (x64/ARM64)
├── SystemArchitecture        # Detected system architecture code
└── ProcessorName             # Processor name for diagnostics
```

### Log Files

BootstrapMate creates detailed logs:
- **Location**: `C:\ProgramData\ManagedBootstrap\logs\`
- **Format**: `YYYY-MM-DD-HHmmss.log`
- **Content**: Detailed execution logs with timestamps

### Common Issues

1. **Architecture Mismatch**: Deploy separate packages for x64 and ARM64
2. **Certificate Issues**: Ensure your code signing certificate is deployed via Intune
3. **Network Connectivity**: Manifest URL must be accessible during ESP
4. **Permission Issues**: BootstrapMate automatically elevates to administrator

### Status Checking

Use this PowerShell command to check BootstrapMate status on a device:

```powershell
# Check BootstrapMate status
$regPath = "HKLM:\SOFTWARE\BootstrapMate"
if (Test-Path $regPath) {
    Get-ItemProperty -Path $regPath | Format-List
} else {
    Write-Host "BootstrapMate registry not found - never installed or completed"
}

# Check detailed status
& "$env:ProgramFiles\BootstrapMate\installapplications.exe" --status
```

## Version Management

### Updating BootstrapMate

1. **Build new version** with updated version number
2. **Update detection script** with new version number
3. **Create new Win32 app** or update existing with supersedence
4. **Deploy to test group** first
5. **Monitor deployment** using Intune reporting
6. **Roll out** to production groups

### Version Numbering

BootstrapMate uses format: `YYYY.MM.DD.HHMM`
- Example: `2025.08.30.1300` (August 30, 2025, 1:00 PM)

## Security Considerations

1. **Code Signing**: Always sign BootstrapMate executable with your enterprise certificate
2. **HTTPS**: Use HTTPS for all manifest and package URLs
3. **Certificate Deployment**: Deploy your code signing certificate via Intune before BootstrapMate
4. **Manifest Security**: Protect your bootstrap manifest URL from unauthorized access
5. **Package Integrity**: Consider implementing hash verification for downloaded packages

## Best Practices

1. **Test Architecture Combinations**: Test on both x64 and ARM64 devices
2. **Monitor Deployments**: Use Intune device compliance and app installation reports
3. **Staged Rollout**: Deploy to pilot groups before full production
4. **Backup Strategy**: Maintain previous working versions for rollback
5. **Documentation**: Document your manifest structure and package dependencies
6. **Regular Updates**: Keep BootstrapMate updated for security and functionality improvements

## Support and Documentation

- **BootstrapMate Repository**: [GitHub Repository URL]
- **Build Documentation**: See `build.ps1` for building and signing instructions
- **Registry Contract**: See `README.md` for complete registry documentation
- **Example Manifests**: See `examples/` directory for sample configurations

This guide provides everything needed to implement BootstrapMate in your Intune environment for reliable, automated Windows device provisioning.
