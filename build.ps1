# BootstrapMate Build Script
# Builds and optionally signs the BootstrapMate executable for deployment
#
# SECURITY NOTE: Code signing is REQUIRED by default for enterprise deployment
# Use -AllowUnsigned only for development builds (NOT for production)
#
# Examples:
#   .\build.ps1                          # Build with signing (certificate auto-detected)
#   .\build.ps1 -Thumbprint "ABC123..."  # Build with specific certificate
#   .\build.ps1 -AllowUnsigned           # Development build without signing (NOT for production)

[CmdletBinding()]
param(
    [switch]$Sign,
    [string]$Thumbprint,
    [ValidateSet("x64", "arm64", "both")]
    [string]$Architecture = "both",
    [switch]$Clean,
    [switch]$Test,
    [switch]$AllowUnsigned  # Explicit flag to allow unsigned builds for development
)

$ErrorActionPreference = "Stop"

# Enterprise Certificate Configuration
$Global:EnterpriseCertCN = 'EmilyCarrU Intune Windows Enterprise Certificate'

Write-Host "=== BootstrapMate Build Script ===" -ForegroundColor Magenta
Write-Host "Architecture: $Architecture" -ForegroundColor Yellow
Write-Host "Code Signing: $(if ($Sign -or !$AllowUnsigned) { 'Required' } else { 'Disabled' })" -ForegroundColor Yellow
Write-Host "Clean Build: $Clean" -ForegroundColor Yellow
Write-Host "Allow Unsigned: $AllowUnsigned" -ForegroundColor $(if ($AllowUnsigned) { "Red" } else { "Green" })
Write-Host ""

# Function to display messages with different log levels
function Write-Log {
    param (
        [string]$Message,
        [ValidateSet("INFO", "WARN", "ERROR", "SUCCESS")]
        [string]$Level = "INFO"
    )
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    switch ($Level) {
        "INFO"    { Write-Host "[$timestamp] [INFO] $Message" -ForegroundColor White }
        "WARN"    { Write-Host "[$timestamp] [WARN] $Message" -ForegroundColor Yellow }
        "ERROR"   { Write-Host "[$timestamp] [ERROR] $Message" -ForegroundColor Red }
        "SUCCESS" { Write-Host "[$timestamp] [SUCCESS] $Message" -ForegroundColor Green }
    }
}

# Function to check if a command exists
function Test-Command {
    param([string]$Command)
    return [bool](Get-Command $Command -ErrorAction SilentlyContinue)
}

# Function to ensure signtool is available
function Test-SignTool {
    $c = Get-Command signtool.exe -ErrorAction SilentlyContinue
    if ($c) { return }
    $roots = @(
        "$env:ProgramFiles\Windows Kits\10\bin",
        "$env:ProgramFiles(x86)\Windows Kits\10\bin"
    ) | Where-Object { Test-Path $_ }

    try {
        $kitsRoot = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots' -EA Stop).KitsRoot10
        if ($kitsRoot) { $roots += (Join-Path $kitsRoot 'bin') }
    } catch {}

    foreach ($root in $roots) {
        $cand = Get-ChildItem -Path (Join-Path $root '*\x64\signtool.exe') -EA SilentlyContinue |
                Sort-Object LastWriteTime -Desc | Select-Object -First 1
        if ($cand) {
            $env:Path = "$($cand.Directory.FullName);$env:Path"
            return
        }
    }
    throw "signtool.exe not found. Install Windows 10/11 SDK (Signing Tools)."
}

# Function to find signing certificate
function Get-SigningCertificate {
    param([string]$Thumbprint = $null)
    
    if ($Thumbprint) {
        $cert = Get-ChildItem -Path "Cert:\CurrentUser\My\$Thumbprint" -ErrorAction SilentlyContinue
        if ($cert) {
            return $cert
        }
        Write-Log "Certificate with thumbprint $Thumbprint not found" "WARN"
    }
    
    # Search for enterprise certificate by common name
    $cert = Get-ChildItem -Path "Cert:\CurrentUser\My\" | Where-Object {
        $_.Subject -like "*$Global:EnterpriseCertCN*"
    } | Select-Object -First 1
    
    if ($cert) {
        Write-Log "Found enterprise certificate: $($cert.Subject)" "SUCCESS"
        Write-Log "Thumbprint: $($cert.Thumbprint)" "INFO"
        
        return $cert
    }
    
    Write-Log "No suitable signing certificate found" "WARN"
    return $null
}

# Function to sign executable with robust retry and multiple timestamp servers
function Invoke-SignArtifact {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Thumbprint,
        [int]$MaxAttempts = 4
    )

    if (-not (Test-Path -LiteralPath $Path)) { throw "File not found: $Path" }

    $tsas = @(
        'http://timestamp.digicert.com',
        'http://timestamp.sectigo.com',
        'http://timestamp.entrust.net/TSS/RFC3161sha2TS'
    )

    $attempt = 0
    while ($attempt -lt $MaxAttempts) {
        $attempt++
        foreach ($tsa in $tsas) {
            & signtool.exe sign `
                /sha1 $Thumbprint `
                /fd SHA256 `
                /td SHA256 `
                /tr $tsa `
                /v `
                "$Path"
            $code = $LASTEXITCODE

            if ($code -eq 0) {
                # Optional append of legacy timestamp for old verifiers; harmless if TSA rejects.
                & signtool.exe timestamp /t http://timestamp.digicert.com /v "$Path" 2>$null
                return
            }

            Start-Sleep -Seconds (4 * $attempt)
        }
    }

    throw "Signing failed after $MaxAttempts attempts across TSAs: $Path"
}

# Function to sign executable
function Sign-Executable {
    param(
        [string]$FilePath,
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$Certificate
    )

    if (-not (Test-Path $FilePath)) {
        Write-Log "File not found for signing: $FilePath" "ERROR"
        return $false
    }

    Write-Log "Signing executable: $([System.IO.Path]::GetFileName($FilePath))" "INFO"

    # Check if file is locked by trying to open it exclusively
    try {
        $fileStream = [System.IO.File]::Open($FilePath, 'Open', 'Read', 'None')
        $fileStream.Close()
    }
    catch {
        Write-Log "File appears to be locked: $FilePath. Attempting advanced unlock..." "WARN"
        
        # Try to identify and terminate processes locking this file
        try {
            # Use handle.exe if available to identify locking processes
            if (Get-Command "handle.exe" -ErrorAction SilentlyContinue) {
                $handleOutput = & handle.exe $FilePath 2>$null
                if ($handleOutput -and $handleOutput -match "pid: (\d+)") {
                    $lockingPids = [regex]::Matches($handleOutput, "pid: (\d+)") | ForEach-Object { $_.Groups[1].Value }
                    foreach ($procId in $lockingPids) {
                        try {
                            $process = Get-Process -Id $procId -ErrorAction SilentlyContinue
                            if ($process) {
                                Write-Log "Terminating process $($process.Name) (PID: $procId) that may be locking $FilePath" "INFO"
                                $process | Stop-Process -Force -ErrorAction SilentlyContinue
                                Start-Sleep -Seconds 1
                            }
                        }
                        catch {
                            # Ignore errors when killing processes
                        }
                    }
                }
            }
        }
        catch {
            # Ignore handle.exe errors
        }
        
        # Multiple attempts with increasing delays
        $unlockAttempts = 3
        for ($attempt = 1; $attempt -le $unlockAttempts; $attempt++) {
            Start-Sleep -Seconds ($attempt * 2)
            
            # Force garbage collection
            [System.GC]::Collect()
            [System.GC]::WaitForPendingFinalizers()
            [System.GC]::Collect()
            
            try {
                $fileStream = [System.IO.File]::Open($FilePath, 'Open', 'Read', 'None')
                $fileStream.Close()
                Write-Log "File unlocked after $attempt attempts: $FilePath" "SUCCESS"
                break
            }
            catch {
                if ($attempt -eq $unlockAttempts) {
                    Write-Log "File still locked after $unlockAttempts attempts: $FilePath. Skipping signing." "WARN"
                    return $false
                }
            }
        }
    }

    # Check if running as administrator
    $currentUser = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = New-Object Security.Principal.WindowsPrincipal($currentUser)
    $isAdmin = $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

    if (-not $isAdmin) {
        Write-Log "Code signing requires administrator privileges. Please run PowerShell as Administrator." "ERROR"
        throw "Access denied: Administrator privileges required for code signing"
    }

    # Use the robust signing function
    try {
        Invoke-SignArtifact -Path $FilePath -Thumbprint $Certificate.Thumbprint
        Write-Log "Successfully signed: $([System.IO.Path]::GetFileName($FilePath))" "SUCCESS"

        # Verify signature
        Write-Log "Verifying signature..." "INFO"
        $null = & signtool verify /pa $FilePath
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Signature verification successful" "SUCCESS"
            return $true
        } else {
            Write-Log "Signature verification failed" "ERROR"
            return $false
        }
    } catch {
        Write-Log "Error during signing: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

# Function to build for specific architecture
function Build-Architecture {
    param(
        [string]$Arch,
        [System.Security.Cryptography.X509Certificates.X509Certificate2]$SigningCert = $null
    )
    
    Write-Log "Building for $Arch architecture..." "INFO"
    
    $outputDir = "publish\$Arch"
    
    if ($Clean -and (Test-Path $outputDir)) {
        Write-Log "Cleaning output directory: $outputDir" "INFO"
        Remove-Item -Path $outputDir -Recurse -Force
    }
    
    # Ensure output directory exists
    if (-not (Test-Path $outputDir)) {
        New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
    }
    
    # Build arguments
    $buildArgs = @(
        "publish"
        "BootstrapMate.csproj"
        "--configuration", "Release"
        "--runtime", "win-$Arch"
        "--output", $outputDir
        "--self-contained", "true"
        "--verbosity", "minimal"
    )
    
    try {
        Write-Log "Running: dotnet $($buildArgs -join ' ')" "INFO"
        & dotnet @buildArgs
        
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet publish failed with exit code: $LASTEXITCODE"
        }
        
        $executablePath = Join-Path $outputDir "installapplications.exe"
        
        if (-not (Test-Path $executablePath)) {
            throw "Expected executable not found: $executablePath"
        }
        
        # Convert to absolute path for signing
        $executablePath = (Get-Item $executablePath).FullName
        
        $fileInfo = Get-Item $executablePath
        $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
        Write-Log "Build successful: $($fileInfo.Name) ($sizeMB MB)" "SUCCESS"
        
        # Sign the executable - MANDATORY unless explicitly disabled
        if ($SigningCert) {
            # Check for ARM64 system building x64 - fix ownership issue
            $isARM64System = (Get-WmiObject -Class Win32_Processor | Select-Object -First 1).Architecture -eq 12
            if ($isARM64System -and $Arch -eq "x64") {
                Write-Log "ARM64 system detected - fixing x64 binary ownership for signing..." "INFO"
                try {
                    & takeown /f $executablePath | Out-Null
                    if ($LASTEXITCODE -eq 0) {
                        Write-Log "Fixed x64 binary ownership" "SUCCESS"
                    }
                } catch {
                    Write-Log "Could not fix ownership, but continuing..." "WARN"
                }
            }
            
            if (Invoke-SignArtifact -Path $executablePath -Thumbprint $SigningCert.Thumbprint) {
                Write-Log "Code signing completed for $Arch" "SUCCESS"
            } else {
                Write-Log "Code signing failed for $Arch" "ERROR"
                return $false
            }
        } else {
            # This should only happen with -AllowUnsigned flag
            Write-Log "UNSIGNED BUILD: This executable is NOT suitable for production deployment" "WARN"
            Write-Log "Unsigned builds should only be used for development and testing" "WARN"
        }
        
        return $true
        
    } catch {
        Write-Log "Build failed for $Arch`: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

# Function to run basic tests
function Test-Build {
    param([string]$ExecutablePath)
    
    Write-Log "Testing build: $ExecutablePath" "INFO"
    
    if (-not (Test-Path $ExecutablePath)) {
        Write-Log "Executable not found for testing: $ExecutablePath" "ERROR"
        return $false
    }
    
    try {
        # Test version output
        Write-Log "Testing --version command..." "INFO"
        $versionOutput = & $ExecutablePath --version 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Version test passed: $versionOutput" "SUCCESS"
        } else {
            Write-Log "Version test failed with exit code: $LASTEXITCODE" "WARN"
        }
        
        # Test help output  
        Write-Log "Testing --help command..." "INFO"
        $null = & $ExecutablePath --help 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Log "Help test passed" "SUCCESS"
        } else {
            Write-Log "Help test failed with exit code: $LASTEXITCODE" "WARN"
        }
        
        return $true
        
    } catch {
        Write-Log "Testing failed: $($_.Exception.Message)" "ERROR"
        return $false
    }
}

# Function to update version in Program.cs
function Update-Version {
    $programCsPath = Join-Path $PSScriptRoot "Program.cs"
    
    if (-not (Test-Path $programCsPath)) {
        throw "Program.cs not found at: $programCsPath"
    }
    
    # Generate new version in YYYY.MM.DD.HHMM format
    $newVersion = Get-Date -Format "yyyy.MM.dd.HHmm"
    Write-Log "Updating version to: $newVersion" "INFO"
    
    # Read the current file content
    $content = Get-Content $programCsPath -Raw
    
    # Find and replace the version line using regex
    $pattern = 'private static readonly string Version = "[\d.]+";'
    $replacement = "private static readonly string Version = `"$newVersion`";"
    
    if ($content -match $pattern) {
        $updatedContent = $content -replace $pattern, $replacement
        Set-Content -Path $programCsPath -Value $updatedContent -NoNewline
        Write-Log "Version updated successfully in Program.cs" "SUCCESS"
        return $newVersion
    } else {
        Write-Log "Could not find version pattern in Program.cs" "WARN"
        return $null
    }
}

# Main build process
try {
    $rootPath = $PSScriptRoot
    Push-Location $rootPath
    
    # Update version first
    $currentVersion = Update-Version
    if ($currentVersion) {
        Write-Log "Building with version: $currentVersion" "INFO"
    }
    
    # Prerequisites check
    Write-Log "Checking prerequisites..." "INFO"
    
    if (-not (Test-Command "dotnet")) {
        throw ".NET CLI not found. Please install .NET 8 SDK."
    }
    
    $dotnetVersion = & dotnet --version
    Write-Log "Using .NET version: $dotnetVersion" "INFO"
    
    # Handle signing certificate - Code signing is REQUIRED unless explicitly disabled
    $signingCert = $null
    $requireSigning = -not $AllowUnsigned
    
    if ($requireSigning) {
        Write-Log "Code signing is REQUIRED for production builds" "INFO"
        Test-SignTool
        $signingCert = Get-SigningCertificate -Thumbprint $Thumbprint
        if (-not $signingCert) {
            Write-Log "CRITICAL ERROR: Code signing certificate not found!" "ERROR"
            Write-Log "BootstrapMate MUST be signed for enterprise deployment" "ERROR"
            Write-Log "" "ERROR"
            Write-Log "Solutions:" "ERROR"
            Write-Log "1. Install your enterprise code signing certificate" "ERROR"
            Write-Log "2. Specify certificate thumbprint with -Thumbprint parameter" "ERROR"
            Write-Log "3. For development only: use -AllowUnsigned flag (NOT for production)" "ERROR"
            throw "Code signing certificate required but not found. Cannot build unsigned executable for production."
        }
        Write-Log "Code signing certificate found and verified" "SUCCESS"
    } else {
        Write-Log "WARNING: Building unsigned executable for development only" "WARN"
        Write-Log "NEVER deploy unsigned builds to production environments" "WARN"
    }
    
    # Build for requested architectures
    $buildResults = @()
    
    $architectures = switch ($Architecture) {
        "x64"  { @("x64") }
        "arm64" { @("arm64") }
        "both" { @("x64", "arm64") }
    }
    
    foreach ($arch in $architectures) {
        Write-Log "" "INFO"
        $success = Build-Architecture -Arch $arch -SigningCert $signingCert
        $buildResults += @{
            Architecture = $arch
            Success = $success
            Path = "publish\$arch\installapplications.exe"
        }
        
        if ($Test -and $success) {
            $execPath = Join-Path $rootPath "publish\$arch\installapplications.exe"
            Test-Build -ExecutablePath $execPath
        }
    }
    
    # Build summary
    Write-Log "" "INFO"
    Write-Log "=== BUILD SUMMARY ===" "INFO"
    
    $successCount = 0
    $signedCount = 0
    foreach ($result in $buildResults) {
        if ($result.Success) {
            $successCount++
            $fullPath = Join-Path $rootPath $result.Path
            if (Test-Path $fullPath) {
                $fileInfo = Get-Item $fullPath
                $sizeMB = [math]::Round($fileInfo.Length / 1MB, 2)
                
                # Check if file is signed
                $isSigned = $false
                if ($signingCert) {
                    try {
                        $signature = Get-AuthenticodeSignature -FilePath $fullPath
                        $isSigned = ($signature.Status -eq "Valid")
                        if ($isSigned) { $signedCount++ }
                    } catch {
                        $isSigned = $false
                    }
                }
                
                $signStatus = if ($signingCert) { 
                    if ($isSigned) { " [SIGNED ✓]" } else { " [SIGN FAILED ❌]" } 
                } else { 
                    " [UNSIGNED ⚠️ DEV ONLY]" 
                }
                Write-Log "✅ $($result.Architecture): $($result.Path) ($sizeMB MB)$signStatus" "SUCCESS"
            } else {
                Write-Log "✅ $($result.Architecture): Built successfully" "SUCCESS"
            }
        } else {
            Write-Log "❌ $($result.Architecture): Build failed" "ERROR"
        }
    }
    
    Write-Log "" "INFO"
    Write-Log "Built $successCount of $($buildResults.Count) architectures successfully" "INFO"
    
    if ($signingCert) {
        if ($signedCount -eq $successCount) {
            Write-Log "All executables signed with certificate: $($signingCert.Subject)" "SUCCESS"
        } else {
            Write-Log "Signing completed for $signedCount of $successCount executables" "WARN"
            Write-Log "Certificate: $($signingCert.Subject)" "INFO"
        }
    }
    
    if ($successCount -eq $buildResults.Count) {
        Write-Log "All builds completed successfully!" "SUCCESS"
        exit 0
    } else {
        Write-Log "Some builds failed" "ERROR"
        exit 1
    }
    
} catch {
    Write-Log "Build process failed: $($_.Exception.Message)" "ERROR"
    exit 1
} finally {
    Pop-Location
}
