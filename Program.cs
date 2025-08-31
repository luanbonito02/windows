using System;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using Microsoft.Win32;

namespace BootstrapMate
{
    class Program
    {
        private static string LogDirectory = @"C:\ProgramData\ManagedBootstrap\logs";
        
        // Version in YYYY.MM.DD.HHMM format
        private static readonly string Version = "2025.08.30.1724";

        static bool IsRunningAsAdministrator()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        static bool TryRestartAsAdministrator(string[] args)
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "installapplications.exe"),
                    Arguments = string.Join(" ", args),
                    UseShellExecute = true,
                    Verb = "runas",  // Request elevation
                    CreateNoWindow = false
                };

                Logger.Info("BootstrapMate requires administrator privileges. Requesting elevation...");
                Console.WriteLine("BootstrapMate requires administrator privileges. Requesting elevation...");
                
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    Logger.Info($"Elevated process started with PID: {process.Id}");
                    Console.WriteLine("Elevated process started. This instance will now exit.");
                    return true;
                }
                else
                {
                    Logger.Warning("Failed to start elevated process - user may have denied elevation");
                    Console.WriteLine("Failed to start elevated process. User may have denied elevation.");
                    return false;
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Error attempting to restart as administrator: {ex.Message}");
                Console.WriteLine($"Error attempting to restart as administrator: {ex.Message}");
                return false;
            }
        }

        // Legacy WriteLog method for compatibility with StatusManager
        static void WriteLog(string message)
        {
            Logger.Debug(message);
        }

        static int Main(string[] args)
        {
            // Check for verbose mode
            bool verboseMode = args.Any(arg => arg.Equals("--verbose", StringComparison.OrdinalIgnoreCase) || 
                                              arg.Equals("-v", StringComparison.OrdinalIgnoreCase));
            
            Logger.Initialize(LogDirectory, Version, verboseMode);
            Logger.Debug("Main() called with arguments: " + string.Join(" ", args));
            
            // Check if running as administrator
            if (!IsRunningAsAdministrator())
            {
                Logger.Info("BootstrapMate is not running as Administrator");
                Console.WriteLine();
                Console.WriteLine("⚠️  BootstrapMate requires administrator privileges");
                Console.WriteLine("    Package installations need elevated access to install to Program Files,");
                Console.WriteLine("    write to HKLM registry, install services, and manage system components.");
                Console.WriteLine();
                
                // Attempt to restart as administrator
                if (TryRestartAsAdministrator(args))
                {
                    Logger.Info("Successfully launched elevated process. Exiting current instance.");
                    return 0; // Success - elevated process will handle the work
                }
                else
                {
                    Logger.Error("Failed to obtain administrator privileges. Cannot continue.");
                    Console.WriteLine("❌ Failed to obtain administrator privileges. Installation cannot continue.");
                    Console.WriteLine();
                    Console.WriteLine("Please run BootstrapMate as Administrator, or use:");
                    Console.WriteLine($"  sudo {Environment.ProcessPath ?? "installapplications.exe"} {string.Join(" ", args)}");
                    return 1; // Error - elevation failed
                }
            }
            
            Logger.Debug("Running with administrator privileges");
            Console.WriteLine("[+] Running with administrator privileges");
            Console.WriteLine();
            
            return MainAsync(args).GetAwaiter().GetResult();
        }
        
        static async Task<int> MainAsync(string[] args)
        {
            Logger.WriteHeader($"BootstrapMate for Windows v{Version}");
            Console.WriteLine("MDM-agnostic bootstrapping tool for Windows");
            Console.WriteLine("Windows Admins Open Source 2025");
            
            // Clean up old statuses (older than 24 hours) on startup
            try
            {
                StatusManager.CleanupOldStatuses(TimeSpan.FromHours(24));
                Logger.Debug("Cleaned up old installation statuses");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to cleanup old statuses: {ex.Message}");
            }
            
            // Clean up old cached packages (older than 7 days) on startup
            try
            {
                CleanupOldCache(TimeSpan.FromDays(7));
                Logger.Debug("Cleaned up old cached packages");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Failed to cleanup old cache files: {ex.Message}");
            }
            
            // Parse command line arguments
            bool forceDownload = false;
            string manifestUrl = "";
            
            if (args.Length == 0)
            {
                Console.WriteLine("Usage:");
                Console.WriteLine("  installapplications.exe --url <manifest-url>");
                Console.WriteLine("  installapplications.exe --help");
                Console.WriteLine("  installapplications.exe --version");
                Console.WriteLine("  installapplications.exe --status");
                Console.WriteLine("  installapplications.exe --clear-cache");
                Console.WriteLine("  installapplications.exe --url <manifest-url> --force");
                Console.WriteLine("  installapplications.exe --url <manifest-url> --verbose");
                Console.WriteLine();
                Console.WriteLine("Options:");
                Console.WriteLine("  --url <url>     URL to the bootstrapmate.json manifest");
                Console.WriteLine("  --force         Force re-download of all packages (ignore cache)");
                Console.WriteLine("  --verbose       Show detailed logging output");
                Console.WriteLine("  --help          Show this help message");
                Console.WriteLine("  --version       Show version information");
                Console.WriteLine("  --status        Show current installation status");
                Console.WriteLine("  --clear-status  Clear all installation status data");
                Console.WriteLine("  --clear-cache   Clear downloaded package cache");
                return 0;
            }
            
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i].ToLower())
                {
                    case "--help":
                    case "-h":
                        Console.WriteLine("BootstrapMate Help");
                        Console.WriteLine("========================");
                        Console.WriteLine();
                        Console.WriteLine("This tool downloads and processes a bootstrapmate.json manifest file");
                        Console.WriteLine("to automatically install packages during Windows OOBE or setup scenarios.");
                        Console.WriteLine();
                        Console.WriteLine("Usage Examples:");
                        Console.WriteLine("  installapplications.exe --url https://example.com/bootstrap/bootstrapmate.json");
                        Console.WriteLine();
                        Console.WriteLine("Features:");
                        Console.WriteLine("  - Supports multiple package types: MSI, EXE, PowerShell, Chocolatey (.nupkg)");
                        Console.WriteLine("  - Handles setupassistant (OOBE) and userland installation phases");
                        Console.WriteLine("  - Admin privilege escalation for elevated packages");
                        Console.WriteLine("  - Architecture-specific conditional installation");
                        Console.WriteLine("  - Registry-based status tracking for detection scripts");
                        return 0;
                        
                    case "--version":
                    case "-v":
                        Console.WriteLine($"BootstrapMate version {Version}");
                        Console.WriteLine("Built for Windows (.NET 9)");
                        return 0;

                    case "--status":
                        return ShowStatus();

                    case "--clear-status":
                        return ClearStatus();

                    case "--clear-cache":
                        return ClearCache();

                    case "--force":
                        forceDownload = true;
                        break;

                    case "--verbose":
                        // Verbose mode is already handled in Main()
                        break;
                        
                    case "--url":
                        if (i + 1 < args.Length)
                        {
                            manifestUrl = args[i + 1];
                            i++; // Skip the next argument since we consumed it
                        }
                        else
                        {
                            Console.WriteLine("ERROR: --url requires a URL parameter");
                            return 1;
                        }
                        break;
                }
            }

            // Process manifest if URL was provided
            if (!string.IsNullOrEmpty(manifestUrl))
            {
                return await ProcessManifest(manifestUrl, forceDownload);
            }
            
            Console.WriteLine("ERROR: Invalid arguments. Use --help for usage information.");
            return 1;
        }
        
        static async Task<int> ProcessManifest(string manifestUrl, bool forceDownload = false)
        {
            try
            {
                // Clear cache if force download is requested
                if (forceDownload)
                {
                    Logger.Debug("Force download requested - clearing package cache");
                    Logger.Info("Force download requested - clearing package cache");
                    ClearPackageCache();
                }

                // Initialize status tracking
                StatusManager.Initialize(manifestUrl, Version);
                Logger.Debug($"Initialized status tracking with RunId: {StatusManager.GetCurrentRunId()}");

                Logger.Info($"Downloading manifest from: {manifestUrl}");
                
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("User-Agent", $"BootstrapMate/{Version}");
                
                string jsonContent = await httpClient.GetStringAsync(manifestUrl);
                Logger.Debug("Manifest downloaded successfully");
                
                // Parse the JSON manifest
                using var doc = JsonDocument.Parse(jsonContent);
                var root = doc.RootElement;
                
                // Process setupassistant packages first
                if (root.TryGetProperty("setupassistant", out var setupAssistant))
                {
                    StatusManager.SetPhaseStatus(InstallationPhase.SetupAssistant, InstallationStage.Starting);
                    Logger.WriteSection("Processing Setup Assistant packages");
                    StatusManager.SetPhaseStatus(InstallationPhase.SetupAssistant, InstallationStage.Running);
                    
                    try
                    {
                        await ProcessPackages(setupAssistant, "setupassistant", forceDownload);
                        StatusManager.SetPhaseStatus(InstallationPhase.SetupAssistant, InstallationStage.Completed);
                        Logger.Debug("Setup Assistant packages completed successfully");
                    }
                    catch (Exception ex)
                    {
                        StatusManager.SetPhaseStatus(InstallationPhase.SetupAssistant, InstallationStage.Failed, ex.Message, 1);
                        throw; // Re-throw to maintain existing error handling
                    }
                }
                else
                {
                    // Mark as skipped if no setupassistant packages
                    StatusManager.SetPhaseStatus(InstallationPhase.SetupAssistant, InstallationStage.Skipped);
                    Logger.Debug("No Setup Assistant packages found - marked as skipped");
                }
                
                // Process userland packages
                if (root.TryGetProperty("userland", out var userland))
                {
                    StatusManager.SetPhaseStatus(InstallationPhase.Userland, InstallationStage.Starting);
                    Logger.Debug("Processing Userland packages...");
                    Logger.WriteSection("Processing Userland packages");
                    StatusManager.SetPhaseStatus(InstallationPhase.Userland, InstallationStage.Running);
                    
                    try
                    {
                        await ProcessPackages(userland, "userland", forceDownload);
                        StatusManager.SetPhaseStatus(InstallationPhase.Userland, InstallationStage.Completed);
                        Logger.Debug("Userland packages completed successfully");
                    }
                    catch (Exception ex)
                    {
                        StatusManager.SetPhaseStatus(InstallationPhase.Userland, InstallationStage.Failed, ex.Message, 1);
                        throw; // Re-throw to maintain existing error handling
                    }
                }
                else
                {
                    // Mark as skipped if no userland packages
                    StatusManager.SetPhaseStatus(InstallationPhase.Userland, InstallationStage.Skipped);
                    Logger.Debug("No Userland packages found - marked as skipped");
                }

                Logger.Debug("BootstrapMate completed successfully!");
                Logger.WriteCompletion("BootstrapMate completed successfully!");
                
                // Write successful completion to registry for Intune detection
                StatusManager.WriteSuccessfulCompletionRegistry();
                
                // Auto-cleanup package cache after successful completion
                try
                {
                    ClearPackageCache();
                    Logger.Info("Auto-cleanup: Package cache cleared after successful completion");
                }
                catch (Exception cacheEx)
                {
                    Logger.Warning($"Auto-cleanup: Could not clear package cache: {cacheEx.Message}");
                    // Don't fail the entire process if cache cleanup fails
                }
                
                return 0;
            }
            catch (Exception ex)
            {
                Logger.Error($"Error processing manifest: {ex.Message}");
                Logger.Debug($"Stack trace: {ex.StackTrace}");
                Logger.WriteError($"Error processing manifest: {ex.Message}");
                
                // Ensure status is marked as failed on any unhandled exception
                try
                {
                    // Try to determine which phase failed based on current state
                    var setupStatus = StatusManager.GetPhaseStatus(InstallationPhase.SetupAssistant);
                    var userlandStatus = StatusManager.GetPhaseStatus(InstallationPhase.Userland);
                    
                    if (setupStatus.Stage == InstallationStage.Running)
                    {
                        StatusManager.SetPhaseStatus(InstallationPhase.SetupAssistant, InstallationStage.Failed, ex.Message, 1);
                    }
                    else if (userlandStatus.Stage == InstallationStage.Running)
                    {
                        StatusManager.SetPhaseStatus(InstallationPhase.Userland, InstallationStage.Failed, ex.Message, 1);
                    }
                }
                catch
                {
                    // Don't let status update failures mask the original error
                }
                
                return 1;
            }
        }
        
        static async Task ProcessPackages(JsonElement packages, string phase, bool forceDownload = false)
        {
            Logger.Debug($"Processing packages for phase: {phase}");
            
            foreach (var package in packages.EnumerateArray())
            {
                string displayName = "Unknown Package"; // Default value for error handling
                try
                {
                    displayName = package.GetProperty("name").GetString() ?? "Unknown";
                    var url = package.GetProperty("url").GetString() ?? "";
                    var fileName = package.GetProperty("file").GetString() ?? "";
                    var type = package.GetProperty("type").GetString() ?? "";
                    
                    Logger.Debug($"Processing package: {displayName} (Type: {type}, File: {fileName})");
                    Logger.WriteProgress("Processing", displayName);
                    
                    // Check architecture condition if specified
                    if (package.TryGetProperty("condition", out var condition))
                    {
                        var conditionStr = condition.GetString() ?? "";
                        Logger.Debug($"Checking condition: {conditionStr}");
                        
                        // Get actual processor architecture - use RuntimeInformation for accurate detection
                        string actualArchitecture = System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();
                        Logger.Debug($"Detected runtime architecture: {actualArchitecture}");
                        
                        // Skip x64 packages on non-x64 systems 
                        // Note: RuntimeInformation reports "X64" for AMD64/Intel 64-bit, "ARM64" for ARM64
                        if (conditionStr.Contains("architecture_x64") && actualArchitecture != "X64")
                        {
                            Logger.Debug($"Skipping {displayName} - x64 condition not met on {actualArchitecture} architecture");
                            Logger.WriteSkipped($"Skipping - x64 condition not met on {actualArchitecture}");
                            continue;
                        }
                        
                        // Skip ARM64 packages on non-ARM64 systems
                        if (conditionStr.Contains("architecture_arm64") && actualArchitecture != "ARM64")
                        {
                            Logger.Debug($"Skipping {displayName} - ARM64 condition not met on {actualArchitecture} architecture");
                            Logger.WriteSkipped($"Skipping - ARM64 condition not met on {actualArchitecture}");
                            continue;
                        }
                    }
                    
                    await DownloadAndInstallPackage(displayName, url, fileName, type, package, forceDownload);
                    Logger.Debug($"Successfully completed package: {displayName}");
                    Logger.WriteSuccess($"{displayName} installed successfully");
                }
                catch (Exception ex)
                {
                    Logger.Error($"Failed to install package {displayName}: {ex.Message}");
                    Logger.WriteError($"Failed to install package {displayName}: {ex.Message}");
                    // Continue with next package instead of stopping entire process
                    // Note: We don't re-throw because we want to continue with other packages
                }
            }
        }
        
        static async Task DownloadAndInstallPackage(string displayName, string url, string fileName, string type, JsonElement packageInfo, bool forceDownload = false)
        {
            try
            {
                // Create temp download directory
                string tempDir = Path.Combine(Path.GetTempPath(), "BootstrapMate");
                Directory.CreateDirectory(tempDir);
                
                string localPath = Path.Combine(tempDir, fileName);
                
                // Check if file exists and force download if requested
                bool needsDownload = forceDownload || !File.Exists(localPath);
                
                if (needsDownload)
                {
                    Logger.Debug($"Downloading {displayName} from: {url}");
                    Logger.WriteSubProgress("Downloading from", url);
                    
                    using var httpClient = new HttpClient();
                    using var response = await httpClient.GetAsync(url);
                    if (!response.IsSuccessStatusCode)
                    {
                        throw new Exception($"Download failed: {response.StatusCode}");
                    }
                    
                    // Ensure the file stream is completely closed before proceeding
                    {
                        await using var fileStream = File.Create(localPath);
                        await response.Content.CopyToAsync(fileStream);
                        await fileStream.FlushAsync();
                    } // fileStream is disposed here
                    
                    // Add a small delay to ensure file handle is released
                    await Task.Delay(100);
                    
                    var fileInfo = new FileInfo(localPath);
                    Logger.Debug($"Downloaded {displayName} to: {localPath} (Size: {fileInfo.Length / 1024 / 1024:F2} MB)");
                    Logger.WriteSubProgress("Downloaded", $"{fileInfo.Length / 1024 / 1024:F1} MB");
                }
                else
                {
                    Logger.Debug($"Using cached file for {displayName}: {localPath}");
                    Logger.WriteSubProgress("Using cached file", Path.GetFileName(localPath));
                }
                
                // Install based on type
                Logger.Debug($"Installing {displayName} using {type} installer...");
                await InstallPackage(localPath, type, packageInfo);
                
                Logger.Debug($"Successfully installed: {displayName}");
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to install {displayName}: {ex.Message}");
                // Re-throw the exception so the caller knows the installation failed
                throw;
            }
        }
        
        static async Task InstallPackage(string filePath, string type, JsonElement packageInfo)
        {
            Logger.Debug($"Installing package: {filePath} (Type: {type})");
            
            switch (type.ToLower())
            {
                case "powershell":
                case "ps1":
                    await RunPowerShellScript(filePath, packageInfo);
                    break;
                    
                case "msi":
                    await RunMsiInstaller(filePath, packageInfo);
                    break;
                    
                case "exe":
                    await RunExecutable(filePath, packageInfo);
                    break;
                    
                case "nupkg":
                    await RunChocolateyInstall(filePath, packageInfo);
                    break;
                    
                default:
                    Logger.Warning($"Unknown package type: {type}");
                    Logger.WriteWarning($"Unknown package type: {type}");
                    break;
            }
        }
        
        static async Task RunPowerShellScript(string scriptPath, JsonElement packageInfo)
        {
            var args = GetArguments(packageInfo);
            string arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" {string.Join(" ", args)}";
            
            // Since BootstrapMate is already running as admin, all PowerShell scripts should inherit admin privileges
            // This ensures chocolatey and other system installers work properly
            bool needsElevation = true; // Always run elevated since we're in an admin context
            
            WriteLog($"Running PowerShell script: {scriptPath}");
            WriteLog($"Arguments: {arguments}");
            WriteLog($"Elevated: {needsElevation}");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = arguments,
                UseShellExecute = false, // Use CreateProcess to inherit admin privileges
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            Console.WriteLine($"     🔧 Running PowerShell: {arguments}");
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                
                // Capture output for debugging
                if (startInfo.RedirectStandardOutput)
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        WriteLog($"PowerShell output: {output}");
                    }
                }
                
                if (startInfo.RedirectStandardError)
                {
                    string error = await process.StandardError.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        WriteLog($"PowerShell error: {error}");
                    }
                }
                
                WriteLog($"PowerShell script completed with exit code: {process.ExitCode}");
                
                if (process.ExitCode != 0)
                {
                    throw new Exception($"PowerShell script failed with exit code: {process.ExitCode}");
                }
            }
        }
        
        static bool RequiresElevation(string scriptPath, JsonElement packageInfo)
        {
            // Get the script filename to check for known patterns
            string scriptFileName = Path.GetFileName(scriptPath).ToLowerInvariant();
            
            // Get package name/ID for specific package checks
            string packageName = "";
            if (packageInfo.TryGetProperty("name", out var nameProp))
            {
                packageName = nameProp.GetString()?.ToLowerInvariant() ?? "";
            }
            
            string packageId = "";
            if (packageInfo.TryGetProperty("packageid", out var idProp))
            {
                packageId = idProp.GetString()?.ToLowerInvariant() ?? "";
            }
            
            // Scripts that definitely need elevation
            if (scriptFileName.Contains("chocolatey") || 
                scriptFileName.Contains("install-chocolatey") ||
                packageName.Contains("chocolatey") ||
                packageId.Contains("chocolatey"))
            {
                return true;
            }
            
            // Any script that installs system-wide components needs elevation
            if (scriptFileName.Contains("install") && 
                (scriptFileName.Contains("system") || scriptFileName.Contains("global")))
            {
                return true;
            }
            
            // Package manager installers typically need elevation
            if (packageName.Contains("package manager") || 
                packageId.Contains("package-manager"))
            {
                return true;
            }
            
            return false;
        }
        
        static async Task RunMsiInstaller(string msiPath, JsonElement packageInfo)
        {
            var args = GetArguments(packageInfo);
            string arguments = $"/i \"{msiPath}\" /quiet /norestart {string.Join(" ", args)}";
            
            var startInfo = new ProcessStartInfo
            {
                FileName = "msiexec.exe",
                Arguments = arguments,
                UseShellExecute = false, // Inherit admin privileges from parent process
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            WriteLog($"Running MSI installer: {arguments}");
            Console.WriteLine($"     📦 Running MSI installer: {arguments}");
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                WriteLog($"MSI installer completed with exit code: {process.ExitCode}");
                if (process.ExitCode != 0)
                {
                    throw new Exception($"MSI installer failed with exit code: {process.ExitCode}");
                }
            }
        }
        
        static async Task RunExecutable(string exePath, JsonElement packageInfo)
        {
            var args = GetArguments(packageInfo);
            string arguments = string.Join(" ", args);
            
            WriteLog($"Running executable: {exePath} {arguments}");
            Console.WriteLine($"     🔧 Running executable: {exePath} {arguments}");
            
            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,  // Inherit admin privileges from parent process
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                await process.WaitForExitAsync();
                
                // Capture output for debugging
                if (startInfo.RedirectStandardOutput)
                {
                    string output = await process.StandardOutput.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(output))
                    {
                        WriteLog($"Executable output: {output}");
                    }
                }
                
                if (startInfo.RedirectStandardError)
                {
                    string error = await process.StandardError.ReadToEndAsync();
                    if (!string.IsNullOrWhiteSpace(error))
                    {
                        WriteLog($"Executable error: {error}");
                    }
                }
                
                WriteLog($"Executable completed with exit code: {process.ExitCode}");
                
                if (process.ExitCode != 0)
                {
                    throw new Exception($"Executable failed with exit code: {process.ExitCode}");
                }
            }
        }
        
        static async Task EnsureChocolateyInstalled()
        {
            Logger.Debug("Checking if Chocolatey is installed...");
            
            // Find chocolatey executable path  
            string chocoPath = "choco.exe";
            string? chocoInstallPath = Environment.GetEnvironmentVariable("ChocolateyInstall");
            if (!string.IsNullOrEmpty(chocoInstallPath))
            {
                string fullChocoPath = Path.Combine(chocoInstallPath, "bin", "choco.exe");
                if (File.Exists(fullChocoPath))
                {
                    chocoPath = fullChocoPath;
                }
            }
            
            var chocoCheck = new ProcessStartInfo
            {
                FileName = chocoPath,
                Arguments = "--version",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            
            try
            {
                using var checkProcess = Process.Start(chocoCheck);
                if (checkProcess != null)
                {
                    await checkProcess.WaitForExitAsync();
                    if (checkProcess.ExitCode == 0)
                    {
                        Logger.Debug("Chocolatey is already installed");
                        Logger.WriteSubProgress("Chocolatey is already installed");
                        return; // Chocolatey is available
                    }
                }
            }
            catch
            {
                // choco.exe not found, need to install
            }
            
            Logger.Debug("Chocolatey not found. Installing Chocolatey...");
            Logger.WriteSubProgress("Installing Chocolatey package manager");
            
            // Install Chocolatey using the official installation method
            // Use PowerShell with proper elevation for ESP environment
            string installScript = "Set-ExecutionPolicy Bypass -Scope Process -Force; [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072; iex ((New-Object System.Net.WebClient).DownloadString('https://community.chocolatey.org/install.ps1'))";
            
            var chocolateyInstall = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -Command \"{installScript}\"",
                UseShellExecute = true, // Critical for ESP privilege inheritance
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            
            using var installProcess = Process.Start(chocolateyInstall);
            if (installProcess != null)
            {
                await installProcess.WaitForExitAsync();
                
                Logger.Debug($"Chocolatey installation completed with exit code: {installProcess.ExitCode}");
                
                if (installProcess.ExitCode != 0)
                {
                    throw new Exception($"Chocolatey installation failed with exit code: {installProcess.ExitCode}");
                }
                
                Logger.Debug("Chocolatey installed successfully");
                Logger.WriteSubProgress("Chocolatey installed successfully");
                
                // Refresh environment variables to pick up chocolatey PATH
                Logger.Debug("Refreshing environment variables...");
                RefreshEnvironmentPath();
            }
        }
        
        static void RefreshEnvironmentPath()
        {
            try
            {
                // Get the current PATH from the registry (machine and user)
                string machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
                string userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
                string combinedPath = machinePath + ";" + userPath;
                
                // Update the current process PATH
                Environment.SetEnvironmentVariable("PATH", combinedPath, EnvironmentVariableTarget.Process);
                
                Logger.Debug("Environment PATH refreshed");
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not refresh PATH environment variable: {ex.Message}");
                // Continue anyway - chocolatey might still work
            }
        }
        
        static async Task<bool> IsChocolateyPackageInstalled(string packageId)
        {
            try
            {
                // Find chocolatey executable path
                string chocoPath = "choco.exe";
                string? chocoInstallPath = Environment.GetEnvironmentVariable("ChocolateyInstall");
                if (!string.IsNullOrEmpty(chocoInstallPath))
                {
                    string fullChocoPath = Path.Combine(chocoInstallPath, "bin", "choco.exe");
                    if (File.Exists(fullChocoPath))
                    {
                        chocoPath = fullChocoPath;
                    }
                }
                
                // Use 'choco list' to check if package is installed (modern Chocolatey syntax)
                var startInfo = new ProcessStartInfo
                {
                    FileName = chocoPath,
                    Arguments = $"list \"{packageId}\"",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                
                Logger.Debug($"Checking if package '{packageId}' is installed: {chocoPath} {startInfo.Arguments}");
                
                using var process = Process.Start(startInfo);
                if (process != null)
                {
                    await process.WaitForExitAsync();
                    
                    if (process.ExitCode == 0)
                    {
                        string output = await process.StandardOutput.ReadToEndAsync();
                        
                        // Parse the output - if the package is installed, it will be listed
                        // Format is typically: "packagename version"
                        // If not installed, output will be empty or show "0 packages installed"
                        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
                        foreach (var line in lines)
                        {
                            var trimmedLine = line.Trim();
                            if (trimmedLine.StartsWith(packageId, StringComparison.OrdinalIgnoreCase) && 
                                !trimmedLine.Contains("packages installed") &&
                                !trimmedLine.Contains("Chocolatey"))
                            {
                                Logger.Debug($"Package '{packageId}' is installed: {trimmedLine}");
                                return true;
                            }
                        }
                        
                        Logger.Debug($"Package '{packageId}' check output: {output.Trim()}");
                    }
                    else
                    {
                        string error = await process.StandardError.ReadToEndAsync();
                        string output = await process.StandardOutput.ReadToEndAsync();
                        Logger.Warning($"chocolatey list command failed with exit code {process.ExitCode}");
                        Logger.Debug($"Chocolatey list stderr: {error}");
                        Logger.Debug($"Chocolatey list stdout: {output}");
                    }
                }
                
                Logger.Debug($"Package '{packageId}' is not installed");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not check if package '{packageId}' is installed: {ex.Message}");
                // If we can't determine, assume it's not installed and try to install
                return false;
            }
        }
        
        static async Task RunChocolateyInstall(string nupkgPath, JsonElement packageInfo)
        {
            var args = GetArguments(packageInfo);
            
            // First check if chocolatey is installed, install it if missing
            await EnsureChocolateyInstalled();
            
            // Extract package details from the .nupkg file by reading the .nuspec
            string packageDir = Path.GetDirectoryName(nupkgPath) ?? Path.GetTempPath();
            string packageId = "";
            string packageVersion = "";
            
            try
            {
                // Read the .nuspec file from the .nupkg to get the correct package ID and version
                using var archive = ZipFile.OpenRead(nupkgPath);
                var nuspecEntry = archive.Entries.FirstOrDefault(e => e.FullName.EndsWith(".nuspec"));
                
                if (nuspecEntry != null)
                {
                    using var stream = nuspecEntry.Open();
                    using var reader = new StreamReader(stream);
                    string nuspecContent = await reader.ReadToEndAsync();
                    
                    // Parse XML to extract ID and version
                    var doc = System.Xml.Linq.XDocument.Parse(nuspecContent);
                    var ns = doc.Root?.GetDefaultNamespace();
                    
                    if (ns != null)
                    {
                        packageId = doc.Root?.Element(ns + "metadata")?.Element(ns + "id")?.Value ?? "";
                        packageVersion = doc.Root?.Element(ns + "metadata")?.Element(ns + "version")?.Value ?? "";
                    }
                    
                    Logger.Debug($"Extracted from .nuspec: ID='{packageId}', Version='{packageVersion}'");
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"Failed to read package metadata from {nupkgPath}: {ex.Message}");
                // Fallback to filename parsing
                string packageFileName = Path.GetFileNameWithoutExtension(nupkgPath);
                int lastDashIndex = packageFileName.LastIndexOf('-');
                if (lastDashIndex > 0 && lastDashIndex < packageFileName.Length - 1)
                {
                    string potentialVersion = packageFileName.Substring(lastDashIndex + 1);
                    if (potentialVersion.Contains('.'))
                    {
                        packageId = packageFileName.Substring(0, lastDashIndex);
                        packageVersion = potentialVersion;
                    }
                }
                
                if (string.IsNullOrEmpty(packageId))
                {
                    packageId = packageFileName;
                }
                Logger.Debug($"Fallback filename parsing: ID='{packageId}', Version='{packageVersion}'");
            }
            
            if (string.IsNullOrEmpty(packageId))
            {
                throw new Exception($"Could not determine package ID from {nupkgPath}");
            }
            
            // Check if package is already installed and determine the correct action
            bool isInstalled = await IsChocolateyPackageInstalled(packageId);
            string action = isInstalled ? "upgrade" : "install";
            
            Logger.Debug($"Package '{packageId}' is {(isInstalled ? "already installed" : "not installed")} - using '{action}' command");
            Logger.WriteSubProgress($"Package '{packageId}' is {(isInstalled ? "already installed" : "not installed")} - using '{action}' command");
            
            // Use proper chocolatey syntax with smart install/upgrade logic
            // Always use --force (-f) to handle conflicts and ensure package state
            string arguments;
            if (!string.IsNullOrEmpty(packageVersion))
            {
                arguments = $"{action} \"{packageId}\" --source=\"{packageDir}\" --version=\"{packageVersion}\" -y --ignore-checksums --acceptlicense --confirm --force {string.Join(" ", args)}";
            }
            else
            {
                arguments = $"{action} \"{packageId}\" --source=\"{packageDir}\" -y --ignore-checksums --acceptlicense --confirm --force {string.Join(" ", args)}";
            }

            // Find chocolatey executable path
            string chocoPath = "choco.exe";
            string? chocoInstallPath = Environment.GetEnvironmentVariable("ChocolateyInstall");
            if (!string.IsNullOrEmpty(chocoInstallPath))
            {
                string fullChocoPath = Path.Combine(chocoInstallPath, "bin", "choco.exe");
                if (File.Exists(fullChocoPath))
                {
                    chocoPath = fullChocoPath;
                }
            }

            // In ESP environment, BootstrapMate should already be running elevated
            // Use PowerShell to run Chocolatey and capture output for better error reporting
            string powershellCommand = $"& '{chocoPath}' {arguments}";
            
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -Command \"{powershellCommand}\"",
                UseShellExecute = false, // Changed to false to capture output
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            
            Logger.Debug($"Running Chocolatey via PowerShell: {powershellCommand}");
            Logger.WriteSubProgress("Running Chocolatey", $"{action} command");
            
            using var process = Process.Start(startInfo);
            if (process != null)
            {
                // Capture output for better error reporting
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                
                await process.WaitForExitAsync();
                
                var stdout = await outputTask;
                var stderr = await errorTask;
                
                Logger.Debug($"Chocolatey completed with exit code: {process.ExitCode}");
                
                // Always log ALL output for debugging - this is critical for troubleshooting
                if (!string.IsNullOrWhiteSpace(stdout))
                {
                    Logger.Debug($"Chocolatey stdout: {stdout.Trim()}");
                }
                
                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    Logger.Debug($"Chocolatey stderr: {stderr.Trim()}");
                }
                
                if (process.ExitCode != 0)
                {
                    // Enhanced error message with all available details
                    var errorParts = new List<string>();
                    
                    if (!string.IsNullOrWhiteSpace(stderr))
                    {
                        errorParts.Add($"STDERR: {stderr.Trim()}");
                    }
                    
                    if (!string.IsNullOrWhiteSpace(stdout))
                    {
                        // Include full stdout for failed commands
                        errorParts.Add($"STDOUT: {stdout.Trim()}");
                    }
                    
                    string errorDetails = errorParts.Count > 0 ? $" - {string.Join(" | ", errorParts)}" : "";
                    
                    Logger.Error($"Chocolatey install failed: {packageId} (exit code {process.ExitCode}){errorDetails}");
                    
                    throw new Exception($"Chocolatey install failed with exit code: {process.ExitCode}{errorDetails}");
                }
                
                // Log successful installation details if verbose
                if (!string.IsNullOrWhiteSpace(stdout) && stdout.ToLower().Contains("successfully installed"))
                {
                    var lines = stdout.Split('\n');
                    var successLines = lines.Where(l => l.ToLower().Contains("successfully")).ToList();
                    if (successLines.Any())
                    {
                        Logger.Debug($"Chocolatey success: {string.Join(", ", successLines.Select(l => l.Trim()))}");
                    }
                }
            }
        }
        
        static List<string> GetArguments(JsonElement packageInfo)
        {
            var arguments = new List<string>();
            
            if (packageInfo.TryGetProperty("arguments", out var argsProperty) && argsProperty.ValueKind == JsonValueKind.Array)
            {
                foreach (var arg in argsProperty.EnumerateArray())
                {
                    if (arg.ValueKind == JsonValueKind.String)
                    {
                        arguments.Add(arg.GetString() ?? "");
                    }
                }
            }
            
            return arguments;
        }

        static int ShowStatus()
        {
            try
            {
                Console.WriteLine("BootstrapMate Status");
                Console.WriteLine("==========================");
                Console.WriteLine();

                foreach (InstallationPhase phase in Enum.GetValues<InstallationPhase>())
                {
                    var status = StatusManager.GetPhaseStatus(phase);
                    
                    Console.WriteLine($"Phase: {phase}");
                    Console.WriteLine($"  Stage: {status.Stage}");
                    Console.WriteLine($"  Architecture: {status.Architecture}");
                    
                    if (!string.IsNullOrEmpty(status.StartTime))
                        Console.WriteLine($"  Start Time: {status.StartTime}");
                    
                    if (!string.IsNullOrEmpty(status.CompletionTime))
                        Console.WriteLine($"  Completion Time: {status.CompletionTime}");
                    
                    if (status.ExitCode != 0)
                        Console.WriteLine($"  Exit Code: {status.ExitCode}");
                    
                    if (!string.IsNullOrEmpty(status.LastError))
                        Console.WriteLine($"  Last Error: {status.LastError}");
                    
                    if (!string.IsNullOrEmpty(status.RunId))
                        Console.WriteLine($"  Run ID: {status.RunId}");
                    
                    if (!string.IsNullOrEmpty(status.BootstrapUrl))
                        Console.WriteLine($"  Bootstrap URL: {status.BootstrapUrl}");
                    
                    Console.WriteLine();
                }

                // Show global version information
                Console.WriteLine("Completion Status:");
                try
                {
                    var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
                    
                    foreach (var view in views)
                    {
                        try
                        {
                            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                            using var key = baseKey.OpenSubKey(@"SOFTWARE\BootstrapMate");
                            
                            if (key != null)
                            {
                                var lastRunVersion = key.GetValue("LastRunVersion")?.ToString();
                                
                                if (!string.IsNullOrEmpty(lastRunVersion))
                                {
                                    Console.WriteLine($"  Last Run Version ({view}): {lastRunVersion}");
                                    break; // Only show once if found
                                }
                            }
                        }
                        catch
                        {
                            // Continue to next view
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️  Warning: Could not read completion information: {ex.Message}");
                }
                Console.WriteLine();

                // Show registry paths for troubleshooting
                Console.WriteLine("Registry Paths:");
                Console.WriteLine("  Completion Status: HKLM\\SOFTWARE\\BootstrapMate\\LastRunVersion");
                Console.WriteLine("  64-bit Status: HKLM\\SOFTWARE\\BootstrapMate\\Status");
                Console.WriteLine("  32-bit Status: HKLM\\SOFTWARE\\WOW6432Node\\BootstrapMate\\Status");
                Console.WriteLine();
                Console.WriteLine("Status File: C:\\ProgramData\\BootstrapMate\\status.json");

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error retrieving status: {ex.Message}");
                return 1;
            }
        }

        static int ClearStatus()
        {
            try
            {
                Console.WriteLine("Clearing BootstrapMate status...");

                // Clear all phase statuses
                foreach (InstallationPhase phase in Enum.GetValues<InstallationPhase>())
                {
                    try
                    {
                        // Delete registry entries for this phase
                        var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
                        
                        foreach (var view in views)
                        {
                            try
                            {
                                using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                                baseKey.DeleteSubKeyTree($@"SOFTWARE\BootstrapMate\Status\{phase}", false);
                            }
                            catch
                            {
                                // Key might not exist, continue
                            }
                        }
                        
                        Console.WriteLine($"  [+] Cleared {phase} status");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"  ⚠️  Warning: Could not clear {phase} status: {ex.Message}");
                    }
                }

                // Clear version registry entry
                try
                {
                    var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
                    
                    foreach (var view in views)
                    {
                        try
                        {
                            using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                            using var key = baseKey.OpenSubKey(@"SOFTWARE\BootstrapMate", true);
                            if (key != null)
                            {
                                key.DeleteValue("LastRunVersion", false);
                            }
                        }
                        catch
                        {
                            // Values might not exist, continue
                        }
                    }
                    
                    Console.WriteLine("  [+] Cleared completion registry entries");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️  Warning: Could not clear completion registry entries: {ex.Message}");
                }

                // Clear status file
                try
                {
                    var statusFile = @"C:\ProgramData\BootstrapMate\status.json";
                    if (File.Exists(statusFile))
                    {
                        File.Delete(statusFile);
                        Console.WriteLine("  [+] Cleared status file");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"  ⚠️  Warning: Could not clear status file: {ex.Message}");
                }

                Console.WriteLine("\n[+] Status cleanup completed");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error clearing status: {ex.Message}");
                return 1;
            }
        }

        static void ClearPackageCache()
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "BootstrapMate");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                    Logger.Debug($"Cleared package cache directory: {tempDir}");
                }
                else
                {
                    Logger.Debug($"Package cache directory does not exist: {tempDir}");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not clear package cache: {ex.Message}");
            }
        }

        static void CleanupOldCache(TimeSpan maxAge)
        {
            try
            {
                string tempDir = Path.Combine(Path.GetTempPath(), "BootstrapMate");
                if (!Directory.Exists(tempDir))
                {
                    return; // No cache directory exists
                }

                var cutoffTime = DateTime.Now - maxAge;
                var files = Directory.GetFiles(tempDir, "*", SearchOption.AllDirectories);
                int cleanedCount = 0;

                foreach (var file in files)
                {
                    try
                    {
                        var fileInfo = new FileInfo(file);
                        if (fileInfo.LastWriteTime < cutoffTime)
                        {
                            File.Delete(file);
                            cleanedCount++;
                            Logger.Debug($"Cleaned old cache file: {Path.GetFileName(file)}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Warning($"Could not delete old cache file {file}: {ex.Message}");
                    }
                }

                // Try to remove empty directories
                try
                {
                    var directories = Directory.GetDirectories(tempDir, "*", SearchOption.AllDirectories);
                    foreach (var dir in directories.OrderByDescending(d => d.Length)) // Delete deepest first
                    {
                        try
                        {
                            if (!Directory.EnumerateFileSystemEntries(dir).Any())
                            {
                                Directory.Delete(dir);
                                Logger.Debug($"Removed empty cache directory: {dir}");
                            }
                        }
                        catch
                        {
                            // Ignore errors when removing empty directories
                        }
                    }
                }
                catch
                {
                    // Ignore directory cleanup errors
                }

                if (cleanedCount > 0)
                {
                    Logger.Debug($"Cleaned up {cleanedCount} old cache files (older than {maxAge.TotalDays:F1} days)");
                }
            }
            catch (Exception ex)
            {
                Logger.Warning($"Could not cleanup old cache files: {ex.Message}");
            }
        }

        static int ClearCache()
        {
            try
            {
                Console.WriteLine("Clearing BootstrapMate package cache...");
                
                string tempDir = Path.Combine(Path.GetTempPath(), "BootstrapMate");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                    Console.WriteLine($"[+] Cleared package cache: {tempDir}");
                    Logger.Info($"Manually cleared package cache: {tempDir}");
                }
                else
                {
                    Console.WriteLine($"ℹ️  Package cache directory does not exist: {tempDir}");
                    Logger.Info($"Package cache directory does not exist: {tempDir}");
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Error clearing cache: {ex.Message}");
                Logger.Error($"Error clearing cache: {ex.Message}");
                return 1;
            }
        }
    }
}
