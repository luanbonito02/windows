# BootstrapMate for Windows

A lightweight bootstrapping tool for Windows device provisioning that downloads and installs packages during OOBE/ESP or after user login.

## Features

- **Dual Phase Support**: Setup Assistant (pre-login/ESP) and Userland (post-login)
- **Package Types**: MSI, EXE, PowerShell scripts, Chocolatey packages (.nupkg)
- **Registry Status Tracking**: Provides completion status for Intune detection scripts
- **Architecture Support**: x64 and ARM64 with conditional installation
- **Admin Escalation**: Automatic privilege elevation for packages requiring admin rights

## Quick Start

```powershell
# Build the application
.\build.ps1 -Sign

# Run with a manifest URL
.\publish\x64\installapplications.exe --url <url>

# Check status (useful for troubleshooting)
.\publish\x64\installapplications.exe --status

# Clear status (for testing)
.\publish\x64\installapplications.exe --clear-status
```

## Registry Status Contract

BootstrapMate tracks completion status in both 64-bit and 32-bit registry views:

```
HKLM\SOFTWARE\BootstrapMate\LastRunVersion                    # Written only after successful completion
HKLM\SOFTWARE\BootstrapMate\Status\SetupAssistant
HKLM\SOFTWARE\BootstrapMate\Status\Userland
HKLM\SOFTWARE\WOW6432Node\BootstrapMate\Status\SetupAssistant  
HKLM\SOFTWARE\WOW6432Node\BootstrapMate\Status\Userland
```

**Status Values**: `Starting`, `Running`, `Completed`, `Failed`, `Skipped`

**Completion Registry Value** (written only after successful run):
- `LastRunVersion`: BootstrapMate version that successfully completed (e.g., "2025.08.30.1300")

**For Intune Detection**: Use `HKLM\SOFTWARE\BootstrapMate\LastRunVersion` as your detection key.

## Intune Implementation

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

### Intune Win32 App Configuration

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
- **Script file**: Upload the detection script from above

#### Dependencies
- None (BootstrapMate is self-contained)

### Package Structure

Create your Win32 app package with these files:

```
BootstrapMate-Package/
├── installapplications.exe         # BootstrapMate executable (x64 or ARM64)
├── appsettings.json                # Configuration file (optional)
├── install.ps1                     # Installation script (see examples/)
└── detection.ps1                   # Detection script (above)
```

### Deployment Strategy

#### Autopilot Deployment
1. **Create Win32 App**: Package BootstrapMate as described above
2. **Assign to Device Groups**: Target your Autopilot device groups
3. **Set as Required**: Deploy as required during ESP
4. **Configure Dependencies**: Ensure this runs before other software

#### Group Assignments
- **Target**: Device groups (Autopilot devices)
- **Assignment type**: Required
- **Delivery optimization**: Download content in background using HTTP only

#### ESP Configuration
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

### Version Management

#### Updating BootstrapMate
1. **Build new version** with updated version number in `Program.cs`
2. **Update detection script** with new version number
3. **Create new Win32 app** or update existing with supersedence
4. **Deploy to test group** first
5. **Monitor deployment** using Intune reporting
6. **Roll out** to production groups

#### Version Numbering
BootstrapMate uses format: `YYYY.MM.DD.HHMM`
- Example: `2025.08.30.1300` (August 30, 2025, 1:00 PM)

## Overview

BootstrapMate for Windows enables IT administrators to:

- **Bootstrap software deployment** during Windows Setup Assistant (OOBE)
- **Orchestrate package installation** from any web-accessible repository
- **Support multiple package formats** (MSI, EXE, PowerShell, Chocolatey, MSIX)
- **Work with any MDM solution** (Intune, JAMF Pro, Workspace ONE, etc.)
- **Provide real-time feedback** to users and administrators
- **Handle dependencies and ordering** automatically

## How It Works

### Windows OOBE/Autopilot Workflow

1. **MDM Trigger**: MDM system deploys BootstrapMate via Win32 app or script
2. **Service Installation**: BootstrapMate installs itself as a Windows Service
3. **Configuration Download**: Downloads package manifest from configured repository
4. **OOBE Package Installation**: Installs system-level packages during device setup
5. **User Session Packages**: Waits for user login and installs user-specific software
6. **Cleanup and Exit**: Removes itself after successful deployment

### Architecture

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   MDM System    │───►│ InstallApps.exe  │───►│ Package Repo    │
│ (Intune, etc.)  │    │ (Windows Service)│    │ (HTTPS/Azure)   │
└─────────────────┘    └──────────────────┘    └─────────────────┘
                                │
                                ▼
                       ┌──────────────────┐
                       │ Package Manifest │
                       │ (JSON/YAML)      │
                       └──────────────────┘
                                │
                                ▼
                       ┌──────────────────┐
                       │ Software Packages│
                       │ MSI/EXE/PS1/MSIX │
                       └──────────────────┘
```

## Quick Start

### 1. Deploy via MDM (Intune Example)

```powershell
# Deploy as Win32 app or PowerShell script
$installCommand = "installapplications.exe --repo https://yourrepo.com/packages --bootstrap"
```

### 2. Package Manifest Structure

```json
{
  "packages": [
    {
      "name": "Microsoft Teams",
      "type": "msi",
      "url": "https://repo.com/packages/teams.msi",
      "arguments": "/quiet ALLUSERS=1",
      "phase": "setupassistant",
      "required": true
    },
    {
      "name": "Adobe Reader",
      "type": "exe", 
      "url": "https://repo.com/packages/reader.exe",
      "arguments": "/S",
      "phase": "userland",
      "dependencies": ["Microsoft Teams"]
    }
  ]
}
```

### 3. Supported Package Types

- **MSI**: Windows Installer packages
- **EXE**: Executable installers
- **PowerShell**: `.ps1` scripts with elevation
- **Chocolatey**: `.nupkg` packages
- **MSIX**: Modern Windows packages
- **Registry**: Registry modifications
- **File Copy**: Direct file deployment

## Features

### Core Functionality
- Windows Service architecture
- OOBE/Autopilot integration
- Multiple package format support
- Dependency resolution
- Progress reporting
- Error handling and retry logic
- Cleanup and self-removal

### Planned Features
- GUI progress window
- Advanced logging and telemetry
- Package verification (signatures, hashes)
- Rollback capabilities
- Configuration profiles
- Integration with popular MDM systems

## Installation

### Prerequisites
- Windows 10/11 (1809 or later)
- .NET 8 Runtime
- Administrative privileges

### Command Line Options

```powershell
installapplications.exe [OPTIONS]

Options:
  --repo <url>              Package repository URL
  --bootstrap               Install and start service
  --config <path>           Custom configuration file
  --phase <phase>           Run specific phase (setupassistant, userland)
  --dry-run                 Test mode without actual installation
  --verbose                 Enable detailed logging
  --uninstall               Remove service and cleanup
  --help                    Show help information
```

## Configuration

### Repository Structure
```
repository/
├── manifest.json          # Package definitions
├── packages/              # Package files
│   ├── teams.msi
│   ├── reader.exe
│   └── scripts/
│       └── setup.ps1
└── config/                # Configuration files
    └── settings.json
```

### Manifest Schema
```json
{
  "$schema": "https://raw.githubusercontent.com/windowsadmins/bootstrapmate/main/schema.json",
  "version": "1.0",
  "packages": [
    {
      "name": "string",           // Package display name
      "type": "msi|exe|ps1|nupkg|msix|registry|file",
      "url": "string",            // Download URL
      "hash": "string",           // SHA256 hash (optional)
      "arguments": "string",      // Installation arguments
      "phase": "setupassistant|userland",
      "required": "boolean",      // Fail deployment if this fails
      "dependencies": ["string"], // Package dependencies
      "conditions": {             // Installation conditions
        "os_version": ">=10.0.19041",
        "architecture": "x64|arm64",
        "domain_joined": true
      }
    }
  ],
  "settings": {
    "timeout": 3600,             // Package timeout in seconds
    "retries": 3,                // Retry attempts
    "cleanup": true,             // Remove downloaded files
    "reboot_required": false     // Reboot after completion
  }
}
```

## Development

### Building from Source

```powershell
# Clone repository
git clone https://github.com/bootstrapmate/bootstrapmate-win.git
cd bootstrapmate-win

# Build with signing
.\build.ps1 -Sign

# Build specific architecture
.\build.ps1 -Architecture x64 -Sign

# Build and test
.\build.ps1 -Sign -Test
```

### Build Script Options

- `-Sign`: Sign executables with enterprise certificate
- `-Architecture`: Target architecture (x64, arm64, both)
- `-Clean`: Clean build directories before building
- `-Test`: Run basic functionality tests after building
- `-Thumbprint`: Specific certificate thumbprint to use

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

### Project Structure

```
src/
├── BootstrapMate.Core/     # Core business logic
├── BootstrapMate.Service/  # Windows Service
├── BootstrapMate.CLI/      # Command line interface
├── BootstrapMate.Common/   # Shared utilities
└── BootstrapMate.Tests/    # Unit tests
```

## Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/amazing-feature`)
3. Commit your changes (`git commit -m 'Add amazing feature'`)
4. Push to the branch (`git push origin feature/amazing-feature`)
5. Open a Pull Request

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- Original [InstallApplications](https://github.com/macadmins/installapplications) macOS project
- [Swift port](https://github.com/rodchristiansen/installapplications) by Rod Christiansen
- Windows Admin community for feedback and testing

## Support

- 📚 [Documentation](https://github.com/bootstrapmate/bootstrapmate-win/wiki)
- 🐛 [Issue Tracker](https://github.com/bootstrapmate/bootstrapmate-win/issues)
- 💬 [Discussions](https://github.com/bootstrapmate/bootstrapmate-win/discussions)
- 📖 [Examples](https://github.com/bootstrapmate/bootstrapmate-win/tree/main/examples)
