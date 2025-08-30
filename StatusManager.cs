using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Win32;

namespace BootstrapMate
{
    public enum InstallationStage
    {
        Starting,
        Running,
        Completed,
        Failed,
        Skipped
    }

    public enum InstallationPhase
    {
        SetupAssistant,
        Userland
    }

    public class InstallationStatus
    {
        public InstallationStage Stage { get; set; }
        public string StartTime { get; set; } = "";
        public string CompletionTime { get; set; } = "";
        public int ExitCode { get; set; }
        public string Version { get; set; } = "2025.08.30.1300";
        public InstallationPhase Phase { get; set; }
        public string Architecture { get; set; } = "";
        public string BootstrapUrl { get; set; } = "";
        public string LastError { get; set; } = "";
        public string RunId { get; set; } = "";
    }

    public static class StatusManager
    {
        private const string BASE_REGISTRY_PATH = @"SOFTWARE\BootstrapMate\Status";
        private const string VERSION_REGISTRY_PATH = @"SOFTWARE\BootstrapMate";
        private const string STATUS_FILE_PATH = @"C:\ProgramData\BootstrapMate\status.json";
        
        private static string _currentRunId = Guid.NewGuid().ToString();
        private static string _bootstrapUrl = "";
        private static string _version = "1.0.0"; // Default fallback version

        public static void Initialize(string bootstrapUrl = "", string version = "1.0.0")
        {
            _bootstrapUrl = bootstrapUrl;
            _version = version;
            _currentRunId = Guid.NewGuid().ToString();
            
            // Ensure status directory exists
            var statusDir = Path.GetDirectoryName(STATUS_FILE_PATH);
            if (statusDir != null && !Directory.Exists(statusDir))
            {
                Directory.CreateDirectory(statusDir);
            }
            
            // Don't write version to registry here - only write it after successful completion
        }

        public static string GetCurrentRunId()
        {
            return _currentRunId;
        }

        public static void SetPhaseStatus(InstallationPhase phase, InstallationStage stage, string errorMessage = "", int exitCode = 0)
        {
            try
            {
                var status = new InstallationStatus
                {
                    Stage = stage,
                    Version = _version,
                    Phase = phase,
                    Architecture = GetArchitecture(),
                    BootstrapUrl = _bootstrapUrl,
                    RunId = _currentRunId,
                    ExitCode = exitCode,
                    LastError = errorMessage
                };

                // Set timestamps based on stage
                switch (stage)
                {
                    case InstallationStage.Starting:
                    case InstallationStage.Running:
                        status.StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        break;
                    case InstallationStage.Completed:
                    case InstallationStage.Failed:
                    case InstallationStage.Skipped:
                        if (string.IsNullOrEmpty(status.StartTime))
                        {
                            status.StartTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        }
                        status.CompletionTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                        break;
                }

                // Write to both registry views (64-bit and 32-bit)
                WriteRegistryStatus(phase, status);

                // Write status file for troubleshooting
                WriteStatusFile(phase, status);

                WriteLog($"Status updated: {phase} = {stage}" + 
                        (exitCode != 0 ? $" (ExitCode: {exitCode})" : "") +
                        (!string.IsNullOrEmpty(errorMessage) ? $" - {errorMessage}" : ""));
            }
            catch (Exception ex)
            {
                WriteLog($"Warning: Failed to update status for {phase}: {ex.Message}");
                // Don't throw - status tracking failure shouldn't break the main process
            }
        }

        public static void WriteSuccessfulCompletionRegistry()
        {
            try
            {
                var values = new Dictionary<string, object>
                {
                    { "LastRunVersion", _version }
                };

                // Write to both 64-bit and 32-bit registry views
                WriteRegistryBothViews(VERSION_REGISTRY_PATH, values);
                
                WriteLog($"Successful completion: LastRunVersion {_version} written to registry");
            }
            catch (Exception ex)
            {
                WriteLog($"Warning: Failed to write completion status to registry: {ex.Message}");
            }
        }

        private static void WriteVersionToRegistry(string version)
        {
            try
            {
                var values = new Dictionary<string, object>
                {
                    { "Version", version },
                    { "LastUpdated", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") },
                    { "Architecture", GetArchitecture() }
                };

                // Write to both 64-bit and 32-bit registry views
                WriteRegistryBothViews(VERSION_REGISTRY_PATH, values);
                
                WriteLog($"Version {version} written to registry");
            }
            catch (Exception ex)
            {
                WriteLog($"Warning: Failed to write version to registry: {ex.Message}");
            }
        }

        private static void WriteRegistryStatus(InstallationPhase phase, InstallationStatus status)
        {
            var phaseName = phase.ToString();
            var subKey = $@"{BASE_REGISTRY_PATH}\{phaseName}";

            var values = new Dictionary<string, object>
            {
                { "Stage", status.Stage.ToString() },
                { "StartTime", status.StartTime },
                { "CompletionTime", status.CompletionTime },
                { "ExitCode", status.ExitCode },
                { "Phase", status.Phase.ToString().ToLowerInvariant() },
                { "Architecture", status.Architecture },
                { "BootstrapUrl", status.BootstrapUrl },
                { "LastError", status.LastError },
                { "RunId", status.RunId }
            };

            // Write to both 64-bit and 32-bit registry views
            WriteRegistryBothViews(subKey, values);
        }

        private static void WriteRegistryBothViews(string subKey, Dictionary<string, object> values)
        {
            var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
            
            foreach (var view in views)
            {
                try
                {
                    using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                    using var key = baseKey.CreateSubKey(subKey, true);
                    
                    if (key != null)
                    {
                        foreach (var kv in values)
                        {
                            if (kv.Value is int intValue)
                            {
                                key.SetValue(kv.Key, intValue, RegistryValueKind.DWord);
                            }
                            else
                            {
                                key.SetValue(kv.Key, kv.Value?.ToString() ?? "", RegistryValueKind.String);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    WriteLog($"Warning: Failed to write to {view} registry: {ex.Message}");
                }
            }
        }

        private static void WriteStatusFile(InstallationPhase phase, InstallationStatus status)
        {
            try
            {
                // Read existing status file or create new structure
                var statusData = new Dictionary<string, InstallationStatus>();
                
                if (File.Exists(STATUS_FILE_PATH))
                {
                    var json = File.ReadAllText(STATUS_FILE_PATH);
                    var existing = JsonSerializer.Deserialize<Dictionary<string, InstallationStatus>>(json);
                    if (existing != null)
                    {
                        statusData = existing;
                    }
                }

                // Update the specific phase
                statusData[phase.ToString()] = status;

                // Write back to file
                var options = new JsonSerializerOptions { WriteIndented = true };
                var updatedJson = JsonSerializer.Serialize(statusData, options);
                File.WriteAllText(STATUS_FILE_PATH, updatedJson);
            }
            catch (Exception ex)
            {
                WriteLog($"Warning: Failed to write status file: {ex.Message}");
            }
        }

        private static string GetArchitecture()
        {
            return RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();
        }

        public static InstallationStatus GetPhaseStatus(InstallationPhase phase)
        {
            try
            {
                var phaseName = phase.ToString();
                var subKey = $@"{BASE_REGISTRY_PATH}\{phaseName}";

                // Try 64-bit view first, then 32-bit
                var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
                
                foreach (var view in views)
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                        using var key = baseKey.OpenSubKey(subKey);
                        
                        if (key != null)
                        {
                            var status = new InstallationStatus
                            {
                                Stage = Enum.TryParse<InstallationStage>(key.GetValue("Stage")?.ToString() ?? "", out var stage) ? stage : InstallationStage.Starting,
                                StartTime = key.GetValue("StartTime")?.ToString() ?? "",
                                CompletionTime = key.GetValue("CompletionTime")?.ToString() ?? "",
                                ExitCode = (int)(key.GetValue("ExitCode") ?? 0),
                                Version = _version, // Use current version instead of stored version
                                Phase = Enum.TryParse<InstallationPhase>(key.GetValue("Phase")?.ToString() ?? "", true, out var phaseValue) ? phaseValue : phase,
                                Architecture = key.GetValue("Architecture")?.ToString() ?? "",
                                BootstrapUrl = key.GetValue("BootstrapUrl")?.ToString() ?? "",
                                LastError = key.GetValue("LastError")?.ToString() ?? "",
                                RunId = key.GetValue("RunId")?.ToString() ?? ""
                            };

                            return status;
                        }
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Warning: Failed to read from {view} registry: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Warning: Failed to get status for {phase}: {ex.Message}");
            }

            // Return default status if not found
            return new InstallationStatus
            {
                Stage = InstallationStage.Starting,
                Phase = phase,
                Architecture = GetArchitecture()
            };
        }

        public static void CleanupOldStatuses(TimeSpan maxAge)
        {
            try
            {
                // Only clean up statuses that are older than maxAge and not "Running"
                foreach (InstallationPhase phase in Enum.GetValues<InstallationPhase>())
                {
                    var status = GetPhaseStatus(phase);
                    
                    if (status.Stage != InstallationStage.Running && 
                        !string.IsNullOrEmpty(status.CompletionTime) &&
                        DateTime.TryParse(status.CompletionTime, out var completionTime) &&
                        DateTime.Now - completionTime > maxAge)
                    {
                        // Clean up this old status
                        DeletePhaseStatus(phase);
                        WriteLog($"Cleaned up old status for {phase} (completed: {completionTime})");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Warning: Failed to cleanup old statuses: {ex.Message}");
            }
        }

        private static void DeletePhaseStatus(InstallationPhase phase)
        {
            try
            {
                var phaseName = phase.ToString();
                var subKey = $@"{BASE_REGISTRY_PATH}\{phaseName}";

                var views = new[] { RegistryView.Registry64, RegistryView.Registry32 };
                
                foreach (var view in views)
                {
                    try
                    {
                        using var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                        baseKey.DeleteSubKeyTree(subKey, false);
                    }
                    catch (Exception ex)
                    {
                        WriteLog($"Warning: Failed to delete {view} registry key: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Warning: Failed to delete status for {phase}: {ex.Message}");
            }
        }

        // Helper method to integrate with existing logging
        private static void WriteLog(string message)
        {
            try
            {
                // Try to use the existing WriteLog method if available through reflection
                var programType = typeof(Program);
                var writeLogMethod = programType.GetMethod("WriteLog", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                
                if (writeLogMethod != null)
                {
                    writeLogMethod.Invoke(null, new object[] { $"[StatusManager] {message}" });
                }
                else
                {
                    // Fallback to console if WriteLog method not found
                    Console.WriteLine($"[StatusManager] {message}");
                }
            }
            catch
            {
                // Silent fallback to console
                Console.WriteLine($"[StatusManager] {message}");
            }
        }
    }
}
