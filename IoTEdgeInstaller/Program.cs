using CommonUtilities;
using Microsoft.Extensions.Logging;
using Microsoft.Win32.TaskScheduler;
using System.Management;

namespace IoTEdgeInstaller
{
    public class Program
    {
        private static readonly string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installation.log");
        private static readonly string paramsFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InstallationCommonApp", "params.txt");
        private static readonly string taskName = "PostRebootInstallerTask";

        static void Main(string[] args)
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<Program>();
            try
            {
                if (!SystemChecks.IsAdministrator(logger))
                {
                    Logger.LogError(logger, "IoTEdgeInstaller", "Application is NOT running in Administrator mode. Please run as Administrator.");
                    Logger.LogWarning(logger, "IoTEdgeInstaller", "The installation encountered an error...");
                    CleanupOnFailure(logger);
                    Console.Read();
                    Environment.Exit(1);
                }
                Logger.LogMessage(logger, "IoTEdgeInstaller", "Application is running in Administrator mode.");

                //Check for existing installations
                Logger.LogMessage(logger, "IoTEdgeInstaller", "Checking for existing installations of Azure IoT Edge and IoTDeviceInstaller...");
                if (IsIoTEdgeInstalled(logger) || IsIoTDeviceInstallerInstalled(logger))
                {
                    Logger.LogError(logger, "IoTEdgeInstaller", "Azure IoT Edge or IoTDeviceInstaller is already installed on this system. Please uninstall the existing installation and try again.");
                    Console.Read();
                    Environment.Exit(1);
                }
                Logger.LogMessage(logger, "IoTEdgeInstaller", "No existing installations found.");

                // Get device name from user and save to params file
                Logger.LogMessage(logger, "IoTEdgeInstaller", "*** Please enter the device name:");
                string? deviceName = Console.ReadLine();

                if (string.IsNullOrEmpty(deviceName))
                {
                    Logger.LogError(logger, "IoTEdgeInstaller", "The value for the <Device Name> missing");
                    Logger.LogWarning(logger, "IoTEdgeInstaller", "The installation encountered an error and could not be completed successfully. A partial installation has occurred. Please uninstall the instance named 'IoT Edge Installer' and try the installation again.");
                    Console.Read();
                    Environment.Exit(1);
                }

                File.WriteAllText(paramsFilePath, deviceName);
                Logger.LogMessage(logger, "IoTEdgeInstaller", $"Device name '{deviceName}' saved to params file.");

                // Check and enable Hyper-V
                try
                {
                    // Check prerequisites                
                    if (!CheckPrerequisites(logger))
                    {
                        Logger.LogError(logger, "IoTEdgeInstaller", "Prerequisites check failed. Exiting...");
                        Logger.LogWarning(logger, "IoTEdgeInstaller", "The installation encountered an error and could not be completed successfully. A partial installation has occurred. Please uninstall the instance named 'IoT Edge Installer' and try the installation again.");
                        Console.Read();
                        Environment.Exit(1);
                    }

                    var hyperVEnabled = CheckAndEnableHyperV(logger);
                    if (hyperVEnabled)
                    {
                        // Hyper-V was already enabled, continue the installation
                        InstallApplication(logger);
                    }
                    else
                    {
                        // Prompt user to save their work
                        if (PromptUserToSaveWork())
                        {
                            // Schedule continuation after reboot and restart the system
                            CreateScheduledTask(logger);
                            RestartSystem(logger);
                        }
                        else
                        {
                            Logger.LogMessage(logger, "IoTEdgeInstaller", "User chose to cancel the restart. Exiting the installer.");
                            Logger.LogWarning(logger, "IoTEdgeInstaller", "The installation has been cancelled by user. A partial installation has occurred. Please uninstall the instance named 'IoT Edge Installer' and try the installation again.");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError(logger, "IoTEdgeInstaller", $"Exception encountered: {ex.Message}");
                    Logger.LogWarning(logger, "IoTEdgeInstaller", "The installation encountered an error and could not be completed successfully. A partial installation has occurred. Please uninstall the instance named 'IoT Edge Installer' and try the installation again.");
                    CleanupOnFailure(logger);
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(logger, "IoTEdgeInstaller", $"Exception encountered: {ex.Message}");
                Logger.LogWarning(logger, "IoTEdgeInstaller", "The installation encountered an error and could not be completed successfully. A partial installation has occurred. Please uninstall the instance named 'IoT Edge Installer' and try the installation again.");
                CleanupOnFailure(logger);
            }

            Console.Read();
        }

        private static bool CheckPrerequisites(ILogger logger)
        {
            Logger.LogMessage(logger, "IoTEdgeInstaller", "Checking prerequisites...");
            // Check Windows version
            Logger.LogMessage(logger, "IoTEdgeInstaller", "Checking Windows version...");
            if (!SystemChecks.CheckWindowsVersion())
            {
                Logger.LogError(logger, "IoTEdgeInstaller", "Unsupported Windows version. Requires Windows 10 version 17763 or higher, or Windows Server 2019/2022.");
                return false;
            }
            Logger.LogMessage(logger, "IoTEdgeInstaller", "Windows version check passed.");

            // Check system architecture
            Logger.LogMessage(logger, "IoTEdgeInstaller", "Checking system architecture...");
            if (!SystemChecks.CheckSystemArchitecture())
            {
                Logger.LogError(logger, "IoTEdgeInstaller", "IoT Edge is only supported on 64-bit architectures.");
                return false;
            }
            Logger.LogMessage(logger, "IoTEdgeInstaller", "System architecture check passed.");

            // Check for internet connectivity
            Logger.LogMessage(logger, "IoTEdgeInstaller", "Checking for internet connectivity...");
            if (!SystemChecks.CheckInternetConnectivity())
            {
                Logger.LogError(logger, "IoTEdgeInstaller", "No internet connectivity. Please ensure the machine has internet access.");
                return false;
            }
            Logger.LogMessage(logger, "IoTEdgeInstaller", "Internet connectivity check passed.");

            // Check memory and disk space
            Logger.LogMessage(logger, "IoTEdgeInstaller", "Checking memory and disk space...");
            if (!CheckMemoryAndDiskSpace(logger))
            {
                Logger.LogError(logger, "IoTEdgeInstaller", "Insufficient memory or disk space.");
                return false;
            }
            Logger.LogMessage(logger, "IoTEdgeInstaller", "Memory and disk space check passed.");

            Logger.LogMessage(logger, "IoTEdgeInstaller", "All prerequisites are met.");
            return true;
        }

        private static bool CheckMemoryAndDiskSpace(ILogger logger)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_ComputerSystem"))
            {
                foreach (var obj in searcher.Get())
                {
                    var memory = Convert.ToInt64(obj["TotalPhysicalMemory"]);
                    if (memory < 2L * 1024 * 1024 * 1024) // 2 GB
                    {
                        Logger.LogError(logger, "IoTEdgeInstaller", "Insufficient memory space.");
                        return false;
                    }
                }
            }

            DriveInfo drive = new("C");
            if (drive.AvailableFreeSpace >= 20L * 1024 * 1024 * 1024) //20GB
            {
                return true;
            }
            else
            {
                Logger.LogError(logger, "IoTEdgeInstaller", "Insufficient disk space.");
                return false;
            }
        }

        private static bool IsIoTEdgeInstalled(ILogger logger)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            Logger.LogMessage(logger, "IoTEdgeInstaller", "Checking existing installation of Azure IoT Edge");
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Product WHERE Name = 'Azure IoT Edge LTS'"))
            {
                return searcher.Get().Count > 0;
            }
        }

        private static bool IsIoTDeviceInstallerInstalled(ILogger logger)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            Logger.LogMessage(logger, "IoTEdgeInstaller", "Checking existing installation of IoTDeviceInstaller");
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Product WHERE Name = 'IoT Edge Installer'"))
            {
                return searcher.Get().Count > 0;
            }
        }

        static bool CheckAndEnableHyperV(ILogger logger)
        {
            Logger.LogMessage(logger, "IoTEdgeInstaller", "Checking if Hyper-V is enabled...");

            var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "CheckAndEnableHyperV.ps1");
            var process = ProcessHelper.StartProcess("powershell.exe", $"-ExecutionPolicy Bypass -File \"{scriptPath}\"", logger);
            process.WaitForExit();
            Logger.LogMessage(logger, "IoTEdgeInstaller", $"Hyper-V is enable exit code: {process.ExitCode}...");
            return process.ExitCode != 3010; // 3010 means a reboot is required
        }

        private static void InstallApplication(ILogger logger)
        {
            Logger.LogMessage(logger, "IoTEdgeInstaller", "Installing the application...");
            using var cts = new CancellationTokenSource(TimeSpan.FromHours(1)); // Add reasonable timeout

            try
            {
                // Define the path to the InstallationCommonApp executable
                var commonAppPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "InstallationCommonApp", "InstallationCommonApp.exe");

                // Ensure the executable exists before trying to run it
                if (!File.Exists(commonAppPath))
                {
                    Logger.LogError(logger, "IoTEdgeInstaller", $"InstallationCommonApp executable not found at {commonAppPath}");
                    return;
                }

                // Properly quote the params file path
                var quotedParamsFilePath = $"\"{paramsFilePath}\"";

                // Start the process
                var process = ProcessHelper.StartProcess(commonAppPath, quotedParamsFilePath, logger);
                if (!process.WaitForExit(TimeSpan.FromHours(1)))
                {
                    process.Kill();
                    throw new TimeoutException("Installation process timed out");
                }

                if (process.ExitCode != 0)
                {
                    throw new Exception($"Installation failed with exit code: {process.ExitCode}");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(logger, "IoTEdgeInstaller", $"Installation failed: {ex.Message}");
                throw;
            }
        }

        static bool PromptUserToSaveWork()
        {
            Console.WriteLine("Confirmation Required");
            Console.WriteLine("Hyper-V needs to be enabled, which requires a system restart.");
            Console.WriteLine("Please save all your work before continuing.");
            Console.WriteLine();
            Console.WriteLine("[Y] Yes  [N] No  [?] Help (default is \"Y\"): ");

            while (true)
            {
                var key = Console.ReadKey(true);

                switch (key.Key)
                {
                    case ConsoleKey.Y:
                    case ConsoleKey.Enter:
                        Console.WriteLine("Yes");
                        return true;

                    case ConsoleKey.N:
                        Console.WriteLine("No");
                        return false;

                    case ConsoleKey.F1:
                    case ConsoleKey.H:
                    case ConsoleKey.Help:
                    case ConsoleKey.Oem2:
                        if (key.KeyChar == '?')
                        {
                            Console.WriteLine("?");
                            Console.WriteLine("Y - Continue with the installation and restart the system");
                            Console.WriteLine("N - Cancel the installation");
                            Console.WriteLine("? - Display this help message");
                            Console.WriteLine();
                            Console.Write("[Y] Yes  [N] No  [?] Help (default is \"Y\"): ");
                        }
                        break;

                    default:
                        // Ignore other keys
                        break;
                }
            }
        }

        private static void CreateScheduledTask(ILogger logger)
        {
            using (TaskService ts = new TaskService())
            {
                TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Description = "Runs PostRebootInstallerService after reboot";

                // Add a boot trigger that starts the task 1 minute after the system boots
                td.Triggers.Add(new BootTrigger { Delay = TimeSpan.FromSeconds(30) });

                // Get the path of the PostRebootInstallerService executable
                var execPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PostRebootInstallerService", "PostRebootInstallerService.exe");

                // Properly quote the params file path to pass as an argument
                var quotedParamsFilePath = $"\"{paramsFilePath}\"";

                // Add an action to run the executable with the paramsFilePath as an argument
                td.Actions.Add(new ExecAction(execPath, quotedParamsFilePath, Path.GetDirectoryName(execPath)));

                td.Principal.LogonType = TaskLogonType.InteractiveToken;
                td.Principal.RunLevel = TaskRunLevel.Highest;

                // Set the task to run even if the computer is on battery power
                td.Settings.StopIfGoingOnBatteries = false;
                td.Settings.DisallowStartIfOnBatteries = false;

                ts.RootFolder.RegisterTaskDefinition(taskName, td);

                Logger.LogMessage(logger, "IoTEdgeInstaller", "Task scheduled successfully.");
            }
        }

        static void RestartSystem(ILogger logger)
        {
            var process = ProcessHelper.StartProcess("shutdown.exe", "/r /t 0", logger);
            process.WaitForExit();
        }

        private static void CleanupOnFailure(ILogger logger)
        {
            try
            {
                // Remove scheduled task if exists
                using (var ts = new TaskService())
                {
                    if (ts.RootFolder.Tasks.Exists(taskName))
                        ts.RootFolder.DeleteTask(taskName);
                }

                // Delete params file
                if (File.Exists(paramsFilePath))
                    File.Delete(paramsFilePath);

                // Add other cleanup steps
            }
            catch (Exception ex)
            {
                Logger.LogError(logger, "IoTEdgeInstaller", $"Cleanup failed: {ex.Message}");
            }
        }
    }
}
