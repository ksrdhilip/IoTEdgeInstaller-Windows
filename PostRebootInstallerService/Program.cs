using CommonUtilities;
using Microsoft.Win32.TaskScheduler;
using Task = System.Threading.Tasks.Task;

namespace PostRebootInstallerService
{
    public class Program
    {
        private static string paramsFilePath = "";
        private static readonly string taskName = "IoTEdgePostRebootInstallerTask";

        static async Task Main(string[] args)
        {
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            var logger = loggerFactory.CreateLogger<Program>();
            try
            {
                if (!SystemChecks.IsAdministrator(logger))
                {
                    Logger.LogError(logger, "PostRebootInstallerService", "Application is NOT running in Administrator mode...");
                    Logger.LogMessage(logger, "PostRebootInstallerService", "The installation encountered an error...");
                    Console.Read();
                    Environment.Exit(1);
                }

                paramsFilePath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "InstallationCommonApp", "params.txt"));
                Logger.LogMessage(logger, "PostRebootInstallerService", $"InstallationCommonApp folder path. {paramsFilePath}");

                if (args.Length > 0 && !File.Exists(paramsFilePath))
                {
                    Logger.LogMessage(logger, "PostRebootInstallerService", "Getting InstallationCommonApp folder path from args.");
                    paramsFilePath = args[0];
                    Logger.LogMessage(logger, "PostRebootInstallerService", $"InstallationCommonApp folder path from args. {paramsFilePath}");
                }

                if (!File.Exists(paramsFilePath))
                {
                    Logger.LogError(logger, "PostRebootInstallerService", $"InstallationCommonApp folder is missing. {paramsFilePath}");
                    Console.Read();
                    Environment.Exit(1);
                }

                Logger.LogMessage(logger, "PostRebootInstallerService", "PostRebootInstallerService starting.");

                try
                {
                    await ContinueInstallationWithRetry(logger);
                }
                catch (Exception ex)
                {
                    Logger.LogError(logger, "PostRebootInstallerService", $"Error during service execution: {ex.Message}");
                }

                Logger.LogMessage(logger, "PostRebootInstallerService", "PostRebootInstallerService stopping.");
                RemoveScheduledTask(logger);
            }
            catch (Exception ex)
            {
                Logger.LogError(logger, "PostRebootInstallerService", $"Error: {ex.Message} /r/n {ex.StackTrace}");
                throw;
            }
        }

        private static async Task ContinueInstallationWithRetry(ILogger logger)
        {
            int maxRetries = 3;
            int currentRetry = 0;

            while (currentRetry < maxRetries)
            {
                try
                {
                    ContinueInstallation(logger);
                    return;
                }
                catch (Exception ex)
                {
                    currentRetry++;
                    Logger.LogError(logger, "PostRebootInstallerService", $"Installation attempt {currentRetry} failed: {ex.Message}");

                    if (currentRetry < maxRetries)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(1));
                        Logger.LogMessage(logger, "PostRebootInstallerService", $"Retrying installation (Attempt {currentRetry + 1}/{maxRetries})");
                    }
                }
            }

            throw new Exception($"Installation failed after {maxRetries} attempts");
        }

        private static void ContinueInstallation(ILogger logger)
        {
            Logger.LogMessage(logger, "PostRebootInstallerService", "Continuing the installation after reboot...");

            var commonAppPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "InstallationCommonApp", "InstallationCommonApp.exe");

            if (!File.Exists(commonAppPath))
            {
                Logger.LogError(logger, "PostRebootInstallerService", $"InstallationCommonApp executable not found at {commonAppPath}");
                return;
            }

            var quotedParamsFilePath = $"\"{paramsFilePath}\"";
            var process = ProcessHelper.StartProcess(commonAppPath, quotedParamsFilePath, logger);
            process.WaitForExit();
            Logger.LogMessage(logger, "PostRebootInstallerService", "Installation completed.");
        }

        private static void RemoveScheduledTask(ILogger logger)
        {
            Logger.LogMessage(logger, "PostRebootInstallerService", "Removing the scheduled task...");

            try
            {
                using (TaskService ts = new TaskService())
                {
                    ts.RootFolder.DeleteTask(taskName, false);
                    Logger.LogMessage(logger, "PostRebootInstallerService", "Scheduled task removed successfully.");
                }
            }
            catch (Exception ex)
            {
                Logger.LogError(logger, "PostRebootInstallerService", ex.Message);
                Console.Read();
                throw;
            }
        }
    }
}
