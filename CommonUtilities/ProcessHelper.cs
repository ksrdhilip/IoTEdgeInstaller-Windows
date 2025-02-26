using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CommonUtilities
{
    public static class ProcessHelper
    {
        public static Process StartProcess(string fileName, string arguments, ILogger? logger)
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    Verb = "runas" // This is to run the process as administrator
                }
            };

            try
            {
                process.OutputDataReceived += (sender, args) => Console.WriteLine(args.Data);
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data is not null)
                    {
                        Console.WriteLine("ERROR: " + args.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
            }
            catch (Exception ex)
            {
                Logger.LogError(logger, "ProcessHelper", $"An error occurred while starting the process: {ex.Message}");
            }

            return process;
        }
    }
}