using CommonUtilities;
using Microsoft.Extensions.Logging;
using System.Management;
using System.Security.Principal;
using System.Runtime.InteropServices;
using System.Diagnostics;
using System.IO;

public static class SystemChecks
{
    public static bool IsAdministrator(ILogger logger)
    {
        if (!OperatingSystem.IsWindows())
            return false;

        Logger.LogMessage(logger, "SystemChecks", "Checking if application is running in Administrator mode...");
        using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
        {
            WindowsPrincipal principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    public static bool CheckInternetConnectivity()
    {
        try
        {
            using (var client = new HttpClient())
            {
                var response = client.GetAsync("https://aka.ms").Result;
                return response.IsSuccessStatusCode;
            }
        }
        catch
        {
            return false;
        }
    }

    public static bool CheckSystemArchitecture()
    {
        if (!OperatingSystem.IsWindows())
            return false;

        using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_OperatingSystem"))
        {
            foreach (var obj in searcher.Get())
            {
                var architecture = (string)obj["OSArchitecture"];
                if (architecture.Contains("64-bit"))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public static bool CheckWindowsVersion()
    {
        OperatingSystem os = Environment.OSVersion;
        Version version = os.Version;
        int buildNumber = GetWindowsBuildNumber();
        return version.Major >= 10 && buildNumber >= 17763;
    }

    private static int GetWindowsBuildNumber()
    {
        if (!OperatingSystem.IsWindows())
            return 0;

        using (var regKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
        {
            return regKey != null ? Convert.ToInt32(regKey.GetValue("CurrentBuildNumber")) : 0;
        }
    }

    public static bool ValidateSystemRequirements(ILogger logger)
    {
        try
        {
            var processorCount = Environment.ProcessorCount;
            if (processorCount < 2)
            {
                Logger.LogError(logger, "SystemChecks", "Minimum of 2 CPU cores required");
                return false;
            }

            var memoryInfo = GC.GetGCMemoryInfo();
            var totalAvailableMemoryBytes = memoryInfo.TotalAvailableMemoryBytes;
            if (totalAvailableMemoryBytes < 2L * 1024 * 1024 * 1024) // 2GB
            {
                Logger.LogError(logger, "SystemChecks", "Minimum of 2GB RAM required");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(logger, "SystemChecks", $"Failed to validate system requirements: {ex.Message}");
            return false;
        }
    }

    public static bool ValidateDiskSpace(string path, long requiredSpace, ILogger logger)
    {
        try
        {
            var rootPath = Path.GetPathRoot(path) ?? throw new InvalidOperationException("Could not determine drive root path");
            var driveInfo = new DriveInfo(rootPath);
            if (driveInfo.AvailableFreeSpace < requiredSpace)
            {
                Logger.LogError(logger, "SystemChecks", $"Insufficient disk space. Required: {requiredSpace / (1024 * 1024)}MB, Available: {driveInfo.AvailableFreeSpace / (1024 * 1024)}MB");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            Logger.LogError(logger, "SystemChecks", $"Failed to check disk space: {ex.Message}");
            return false;
        }
    }

    // Other system checks
}