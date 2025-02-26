using CommonUtilities;
using Microsoft.Azure.Devices.Provisioning.Client;
using Microsoft.Azure.Devices.Provisioning.Client.Transport;
using Microsoft.Azure.Devices.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;

namespace InstallationCommonApp
{
    partial class Program
    {
        private static class Configuration
        {
            public static IConfiguration Config { get; }

            static Configuration()
            {
                Config = new ConfigurationBuilder()
                    .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                    .AddJsonFile("appsettings.json", optional: false)
                    .AddEnvironmentVariables()
                    .Build();
            }

            public const string DPSGlobalDeviceEndpoint = "global.azure-devices-provisioning.net";
            public static string DpsScopeId => GetSecureValue("DpsScopeId");
            // Note: In production, these should be stored securely, not as constants
            public static string PrimaryKey => GetSecureValue("PrimaryKey");
            public static string SecondaryKey => GetSecureValue("SecondaryKey");

            private static string GetSecureValue(string key)
            {
                var value = Environment.GetEnvironmentVariable(key) ?? Config[key];
                if (string.IsNullOrEmpty(value))
                    throw new InvalidOperationException($"Configuration value for {key} not found");
                return value;
            }
        }

        private static readonly string logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installation.log");
        private static ILogger<Program>? logger;
        [GeneratedRegex(@"VMSwitchName: ([^\,]+)\,")]
        private static partial Regex FetchVMSwitchName();
        [GeneratedRegex(@"EFLOWVMIP: ([\d\.]+)\,")]
        private static partial Regex FetchEFLOWVMIPAddress();
        [GeneratedRegex(@"GatewayIP: ([\d\.]+)\,")]
        private static partial Regex FetchGatewayIPAddress();
        [GeneratedRegex(@"EFLOWVMIPV4PrefixLength: ([\d\.]+)")]
        private static partial Regex FetchEFLOWVMIPV4PrefixLength();

        private static string eflowVmIPAddress = "192.168.3.5";
        private static string gateWayIPAddress = "192.168.3.1";
        private static string vmSwitchName = "IoTEdgeVSwitch";
        private static string ipV4PrefixLength = "24";
        private static bool isServerOS = false;

        private readonly ILogger<Program> _logger;
        private readonly IConfiguration _configuration;

        public Program(ILogger<Program> logger, IConfiguration configuration)
        {
            _logger = logger;
            _configuration = configuration;
        }

        static async Task Main(string[] args)
        {
            using var cts = new CancellationTokenSource();
            var loggerFactory = LoggerFactory.Create(builder => builder.AddConsole());
            logger = loggerFactory.CreateLogger<Program>();

            try
            {
                if (!await ValidatePrerequisites(cts.Token))
                {
                    LogMessage(ErrorMessages.InstallationFailed);
                    Console.Read();
                    Environment.Exit(1);
                }

                LogMessage("InstallationCommonApp Started.");
                if (args.Length == 0)
                {
                    LogError("No parameters provided.");
                    Console.Read();
                    return;
                }
                LogMessage("Parameters received.");
                string paramsFilePath = args[0];
                LogMessage($"File path from parameters.{paramsFilePath}");
                if (!File.Exists(paramsFilePath))
                {
                    LogError($"Parameters file not found: {paramsFilePath}");
                    Console.Read();
                    return;
                }
                LogMessage($"Reading params file for getting device name.");
                var parameters = File.ReadAllLines(paramsFilePath);
                string deviceName = parameters[0]; // Assuming the first line is deviceName, add more as needed                
                string registrationId = GenerateDeviceRegistrationID(deviceName);

                // Continue with the installation process
                await InstallApplication(registrationId);
            }
            catch (Exception ex)
            {
                LogError($"Error: {ex.Message}");
                Console.Read();
                throw;
            }
        }

        private static bool IsAdministrator()
        {
            LogMessage("Checking if application is running in Administrator mode...");
            if (OperatingSystem.IsWindows())
            {
                using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                {
                    WindowsPrincipal principal = new WindowsPrincipal(identity);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator);
                }
            }
            // For non-Windows platforms, assume false or handle differently
            return false;
        }

        private static string GetHostOSType()
        {
            if (!OperatingSystem.IsWindows())
            {
                return "Unknown";
            }

            using (var searcher = new ManagementObjectSearcher("SELECT Caption FROM Win32_OperatingSystem"))
            using (var results = searcher.Get())
            {
                foreach (var os in results)
                {
                    string caption = os["Caption"]?.ToString() ?? string.Empty;
                    if (caption.Contains("Server"))
                    {
                        return "Server";
                    }
                    else
                    {
                        return "Client";
                    }
                }
            }
            return "Unknown";
        }

        private static void HandleInstallationError(string errorMessage)
        {
            LogError(errorMessage);
            Console.WriteLine("The installation encountered an error and could not be completed successfully. A partial installation has occurred. Please uninstall 'IoT Edge Installer' and try the installation again.");
            Console.Read();
            Environment.Exit(1);
        }

        /// <summary>
        /// Installs the IoT Edge runtime and provisions the device using DPS.
        /// </summary>
        /// <param name="registrationId">The device registration ID.</param>
        /// <param name="progress">Optional progress reporter.</param>
        private static async Task InstallApplication(string registrationId, IProgress<int>? progress = null)
        {
            var state = new InstallationState { RegistrationId = registrationId };
            try
            {
                ReportProgress(0, "Starting installation");
                LogMessage("Installing the components...");
                var osType = GetHostOSType();

                if (osType == "Server")
                {
                    isServerOS = true;
                    if (!await CheckAndAddVMSwitch())
                    {
                        throw new InstallationException("Adding VM Switch failed");
                    }
                    state.VmSwitchCreated = true;
                    ReportProgress(20, "VM Switch created");
                }

                if (!await InstallIoTEdgeRuntimeAsync(CancellationToken.None, progress))
                {
                    throw new InstallationException("IoT Edge runtime installation failed");
                }
                state.IoTEdgeInstalled = true;
                ReportProgress(50, "IoT Edge runtime installed");

                if (!isServerOS)
                {
                    string checkConnectivityScript = "Invoke-EflowVmCommand 'ping -c 1 global.azure-devices-provisioning.net'";
                    string result = "";
                    try
                    {
                        result = await ExecutePowerShellScriptWithOutputAsync(checkConnectivityScript, CancellationToken.None);
                    }
                    catch
                    {
                        result = "";
                    }

                    if (result.Contains("100% packet loss") || result.Contains("unknown host") || String.IsNullOrEmpty(result))
                    {
                        LogMessage("VM cannot reach DPS endpoint. Configuring DNS settings...");
                        if (!await ConfigureEflowDNSAsync())
                        {
                            await RollbackInstallation(state);
                            HandleInstallationError("EFLOW DNS configuration failed.");
                        }
                        state.DnsConfigured = true;
                        ReportProgress(70, "EFLOW DNS configured");
                    }
                    else
                    {
                        LogMessage("VM can reach DPS endpoint. Skipping DNS configuration.");
                    }
                }

                if (!await ProvisionIoTEdgeAsync(Configuration.DpsScopeId, registrationId, ComputeKeyHash(Configuration.PrimaryKey, registrationId)))
                {
                    await RollbackInstallation(state);
                    HandleInstallationError("IoT Edge runtime provisioning failed.");
                }
                ReportProgress(90, "Device provisioned");

                LogMessage("Checking IoT Edge modules status...");
                int maxRetries = 5;
                int currentRetry = 0;
                bool allModulesRunning = false;

                while (currentRetry < maxRetries && !allModulesRunning)
                {
                    string moduleStatus = await ExecutePowerShellScriptWithOutputAsync("Invoke-EflowVmCommand 'sudo iotedge list'", CancellationToken.None);

                    bool edgeAgentRunning = moduleStatus.Contains("edgeAgent") && moduleStatus.Contains("running");
                    bool edgeHubRunning = moduleStatus.Contains("edgeHub") && moduleStatus.Contains("running");

                    if (moduleStatus.Contains("failed") || moduleStatus.Contains("error"))
                    {
                        LogMessage("Warning: Some modules might have deployment issues. Check logs for details.");
                    }

                    if (edgeAgentRunning && edgeHubRunning)
                    {
                        allModulesRunning = true;
                        LogMessage("Core IoT Edge modules (edgeAgent, edgeHub) are running properly.");
                        break;
                    }
                    else
                    {
                        currentRetry++;
                        if (currentRetry < maxRetries)
                        {
                            LogMessage($"Modules not ready (edgeAgent: {edgeAgentRunning}, edgeHub: {edgeHubRunning}). Retrying...");
                            await Task.Delay(TimeSpan.FromMinutes(1));
                        }
                    }
                }

                if (!allModulesRunning)
                {
                    string systemLogs = await ExecutePowerShellScriptWithOutputAsync("Invoke-EflowVmCommand 'sudo iotedge system logs'", CancellationToken.None);
                    LogMessage("System Logs:");
                    LogMessage(systemLogs);
                    await RollbackInstallation(state);
                    HandleInstallationError("Edge Agent module failed to start after multiple retries.");
                }

                await EnableFirewallSettingsForEflowVm();
                string ipAddressOfEflowVm = await GetIPAddressOfEflowVm();
                if (string.IsNullOrEmpty(ipAddressOfEflowVm))
                {
                    LogMessage("Warning: Could not retrieve EFLOW VM IP address. Some features might not work correctly.");
                }
                HtmlHelper.CreateAndOpenSuccessHtml(ipAddressOfEflowVm, "InstallationCommonApp", logger ?? throw new InvalidOperationException("Logger is not initialized"));
                await CleanupInstallationFiles();
                ReportProgress(100, "Installation complete");
            }
            catch (InstallationException ex)
            {
                await RollbackInstallation(state);
                HandleInstallationError(ex.Message);
            }
            catch (Exception ex)
            {
                await RollbackInstallation(state);
                HandleInstallationError($"Unexpected error during installation: {ex.Message}");
            }
        }

        static async Task<bool> CheckAndAddVMSwitch()
        {
            LogMessage("Checking and adding VM Switch");

            try
            {
                var scriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AddVMSwitch.ps1");
                var (eflowVmIp, gateway, vmSwitch, prefixLength) = ParseOutputForIpAndGateway(await ExecutePowerShellScriptWithOutputAsync(scriptPath, CancellationToken.None));
                eflowVmIPAddress = eflowVmIp;
                gateWayIPAddress = gateway;
                vmSwitchName = vmSwitch;
                ipV4PrefixLength = prefixLength;
            }
            catch (Exception ex)
            {
                LogError($"Device registration failed: {ex.Message}");
                return false;
            }
            return true;
        }
        public static (string eflowVmIp, string gateway, string vmSwitchName, string ipV4PrefixLength) ParseOutputForIpAndGateway(string output)
        {
            string eflowVmIp = string.Empty;
            string gateway = string.Empty;
            string vmSwitchName = string.Empty;
            string ipV4PrefixLength = string.Empty;

            var eflowVmIpMatch = FetchEFLOWVMIPAddress().Match(output);
            var gatewayMatch = FetchGatewayIPAddress().Match(output);
            var vmSwitchNameMatch = FetchVMSwitchName().Match(output);
            var ipV4PrefixLengthMatch = FetchEFLOWVMIPV4PrefixLength().Match(output);

            if (eflowVmIpMatch.Success)
            {
                eflowVmIp = eflowVmIpMatch.Groups[1].Value;
            }
            if (gatewayMatch.Success)
            {
                gateway = gatewayMatch.Groups[1].Value;
            }
            if (vmSwitchNameMatch.Success)
            {
                vmSwitchName = vmSwitchNameMatch.Groups[1].Value;
            }
            if (ipV4PrefixLengthMatch.Success)
            {
                ipV4PrefixLength = ipV4PrefixLengthMatch.Groups[1].Value;
            }

            return (eflowVmIp, gateway, vmSwitchName, ipV4PrefixLength);
        }

        private static async Task<bool> InstallIoTEdgeRuntimeAsync(CancellationToken cancellationToken, IProgress<int>? progress = null)
        {
            string msiPath = Path.Combine(Path.GetTempPath(), "AzureIoTEdge.msi");
            try
            {
                LogMessage("Installing IoT Edge runtime...");

                // Clean up any existing installer
                if (File.Exists(msiPath))
                {
                    LogMessage("Removing existing installer file...");
                    File.Delete(msiPath);
                }

                // Download the installer
                LogMessage("Downloading EFLOW installer...");
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

                try
                {
                    string downloadUrl = RuntimeInformation.ProcessArchitecture switch
                    {
                        Architecture.Arm64 => "https://aka.ms/AzEFLOWMSI_1_5_LTS_ARM64",
                        _ => "https://aka.ms/AzEFLOWMSI_1_5_LTS_X64"
                    };

                    var response = await client.GetAsync(downloadUrl);
                    response.EnsureSuccessStatusCode();
                    await using var fs = new FileStream(msiPath, FileMode.CreateNew);
                    await response.Content.CopyToAsync(fs);
                    LogMessage("Download completed successfully.");

                    // Verify file exists and has content
                    var fileInfo = new FileInfo(msiPath);
                    if (!fileInfo.Exists || fileInfo.Length < 1000000) // Basic size check (at least 1MB)
                    {
                        LogError("Download appears to be incomplete or corrupted.");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Failed to download IoT Edge LTS installer: {ex.Message}");
                    return false;
                }

                // Install IoT Edge
                LogMessage("Starting IoT Edge installation...");
                string script = @"Start-Process -Wait msiexec -ArgumentList '/i', $([io.Path]::Combine($env:TEMP, 'AzureIoTEdge.msi')), '/qn'";
                await ExecutePowerShellScriptAsync(script, cancellationToken);

                // Verify installation
                int maxRetries = 3;
                int currentRetry = 0;
                bool installationVerified = false;

                while (currentRetry < maxRetries && !installationVerified)
                {
                    if (IsIoTEdgeInstalled(logger))
                    {
                        installationVerified = true;
                        LogMessage("IoT Edge installation verified successfully.");
                        progress?.Report(75); // 75% complete
                    }
                    else
                    {
                        currentRetry++;
                        if (currentRetry < maxRetries)
                        {
                            LogMessage($"Installation verification attempt {currentRetry} of {maxRetries} failed. Waiting before retry...");
                            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
                        }
                    }
                }

                if (!installationVerified)
                {
                    LogError("IoT Edge LTS installation could not be verified after multiple attempts.");
                    return false;
                }

                // Deploy EFLOW VM
                LogMessage("Deploying EFLOW VM...");
                await DeployEflowVmAsync(cancellationToken);
                progress?.Report(100);
                LogMessage("IoT Edge runtime installation and deployment completed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"EFLOW installation failed: {ex.Message}");
                if (ex is OperationCanceledException)
                {
                    LogMessage("Installation was cancelled by user.");
                }
                return false;
            }
            finally
            {
                // Clean up the installer file
                try
                {
                    if (File.Exists(msiPath))
                    {
                        File.Delete(msiPath);
                        LogMessage("Cleaned up installer file.");
                    }
                }
                catch (Exception ex)
                {
                    LogError($"Failed to clean up installer file: {ex.Message}");
                }
            }
        }

        private static bool IsIoTEdgeInstalled(ILogger logger)
        {
            if (!OperatingSystem.IsWindows())
                return false;

            Logger.LogMessage(logger, "InstallationCommonApp", "Checking existing installation of Azure IoT Edge");
            using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Product WHERE Name = 'Azure IoT Edge LTS'"))
            {
                return searcher.Get().Count > 0;
            }
        }

        private static async Task ExecutePowerShellScriptAsync(string script, CancellationToken cancellationToken, TimeSpan? timeout = null)
        {
            try
            {
                LogMessage($"Executing PowerShell script: {script}");

                using var cts = timeout.HasValue ? new CancellationTokenSource(timeout.Value) : new CancellationTokenSource();
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, cancellationToken);

                ProcessStartInfo startInfo = new()
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = false,
                    Verb = "runas" // This is to run the process as administrator
                };

                using var process = new Process();
                process.StartInfo = startInfo;
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
                await process.WaitForExitAsync(linkedCts.Token);
                if (linkedCts.IsCancellationRequested)
                    throw new TimeoutException($"PowerShell script execution timed out after {timeout?.TotalSeconds} seconds");
            }
            catch (Exception ex)
            {
                LogError($"PowerShell script execution failed: {ex.Message}");
                throw;
            }
        }

        private static async Task<string> GetIPAddressOfEflowVm()
        {
            try
            {
                LogMessage("Getting EFLOW VM IP Address...");
                string script = @"Invoke-EflowVmCommand 'ip -4 addr show eth0' | Select-String -Pattern 'inet\s(\d+\.\d+\.\d+\.\d+)' | ForEach-Object { $_.Matches.Groups[1].Value }";
                string ipAddress = await ExecutePowerShellScriptWithOutputAsync(script, CancellationToken.None);
                LogMessage($"EFLOW VM IP Address: {ipAddress}");
                return ipAddress;
            }
            catch (Exception ex)
            {
                LogError($"Getting EFLOW VM IP Address failed: {ex.Message}");
            }
            return "";
        }

        private static async Task<string> ExecutePowerShellScriptWithOutputAsync(string script, CancellationToken cancellationToken)
        {
            using var process = new Process();
            try
            {
                LogMessage($"Executing PowerShell script: {script}");
                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{script}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas" // This is to run the process as administrator
                };

                StringBuilder outputBuilder = new();
                process.OutputDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        Console.WriteLine(args.Data);
                        outputBuilder.AppendLine(args.Data);
                    }
                };

                StringBuilder errorBuilder = new();
                process.ErrorDataReceived += (sender, args) =>
                {
                    if (args.Data != null)
                    {
                        Console.Error.WriteLine(args.Data);
                        errorBuilder.AppendLine(args.Data);
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                await process.WaitForExitAsync(cancellationToken);

                if (process.ExitCode != 0)
                {
                    LogError($"PowerShell script execution failed: {errorBuilder}");
                    throw new Exception(errorBuilder.ToString());
                }

                return outputBuilder.ToString().Trim();
            }
            finally
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        LogError($"Failed to kill process: {ex.Message}");
                    }
                }
            }
        }


        private static async Task DeployEflowVmAsync(CancellationToken cancellationToken)
        {
            try
            {
                LogMessage("Deploying EFLOW VM...");
                var script = "";
                if (isServerOS)
                {
                    script = $"Deploy-Eflow -cpuCount 2 -memoryInMB 2048 -vmDataSize 20 -vSwitchType \"Internal\" -vSwitchName {vmSwitchName} -ip4Address {eflowVmIPAddress} -ip4GatewayAddress {gateWayIPAddress} -ip4PrefixLength {ipV4PrefixLength} -acceptEula Yes -acceptOptionalTelemetry Yes\r\n";
                }
                else
                {
                    script = "Deploy-Eflow -cpuCount 2 -memoryInMB 2048 -vmDataSize 10 -acceptEula Yes -acceptOptionalTelemetry Yes\r\n";
                }


                await ExecutePowerShellScriptAsync(script, cancellationToken);
                await GetEflowVmInfoAsync(cancellationToken);
                LogMessage("EFLOW VM deployed successfully.");
            }
            catch (Exception ex)
            {
                LogError($"Deploy-Eflow failed: {ex.Message}");
            }
        }

        private static async Task<(bool, string)> RegisterDeviceAsync(string dpsIdScope, string primaryKey, string secondaryKey, string deviceRegistrationId)
        {
            return await RetryWithBackoff(async () =>
            {
                try
                {
                    LogMessage("Registering device in IoT Hub...");

                    LogMessage("Initializing security provider for device registration...");
                    using var securityProvider = new SecurityProviderSymmetricKey(
                        registrationId: deviceRegistrationId,
                        primaryKey: ComputeKeyHash(primaryKey, deviceRegistrationId),
                        secondaryKey: ComputeKeyHash(secondaryKey, deviceRegistrationId));

                    LogMessage("Initializing transport handler...");
                    using var transportHandler = new ProvisioningTransportHandlerAmqp(TransportFallbackType.TcpOnly);

                    LogMessage("Creating provisioning device client...");
                    var provisioningDeviceClient = ProvisioningDeviceClient.Create(
                        globalDeviceEndpoint: Configuration.DPSGlobalDeviceEndpoint,
                        idScope: dpsIdScope,
                        securityProvider: securityProvider,
                        transport: transportHandler);

                    LogMessage("Registering device...");
                    try
                    {
                        var deviceRegistrationResult = await provisioningDeviceClient.RegisterAsync();
                        LogMessage($"Device registration result: {deviceRegistrationResult.Status}");
                        if (!string.IsNullOrEmpty(deviceRegistrationResult.AssignedHub))
                        {
                            LogMessage($"Assigned to hub '{deviceRegistrationResult.AssignedHub}'");
                        }

                        if (deviceRegistrationResult.Status != ProvisioningRegistrationStatusType.Assigned)
                        {
                            Console.WriteLine($"Registration status did not assign a hub, so exiting the device registration and provisioning.");
                            return (false, string.Empty);
                        }

                        LogMessage($"Device {deviceRegistrationResult.DeviceId} registered to {deviceRegistrationResult.AssignedHub}.");
                        LogMessage("Device registered successfully.");
                    }
                    catch (Exception ex)
                    {
                        LogError($"Device registration failed: {ex.Message}");
                        throw;
                    }

                    return (true, securityProvider.GetPrimaryKey());
                }
                catch (Exception ex)
                {
                    LogError($"Device registration failed: {ex.Message}");
                    return (false, string.Empty);
                }
            }, CancellationToken.None);
        }

        private static async Task<bool> ProvisionIoTEdgeAsync(string dpsScopeId, string registrationId, string primaryKey)
        {
            LogMessage("Provisioning EFLOW VM...");
            var provisioningType = "DpsSymmetricKey";
            var script = $"Provision-EflowVm -provisioningType {provisioningType} -ScopeId {dpsScopeId} -RegistrationId {registrationId} -symmKey {primaryKey} -globalEndpoint https://global.azure-devices-provisioning.net";
            await ExecutePowerShellScriptAsync(script, CancellationToken.None);
            await StartEflowVmInfoAsync(CancellationToken.None);
            LogMessage("EFLOW VM provisioned successfully.");
            return true;
        }

        private static async Task GetEflowVmInfoAsync(CancellationToken cancellationToken)
        {
            try
            {
                LogMessage("Getting EFLOW VM info...");
                string script = @"$vmInfo = Get-EflowVm \r\n $vmInfo | ConvertTo-Json";
                await ExecutePowerShellScriptAsync(script, cancellationToken);
                LogMessage("EFLOW VM info retrieved successfully.");
            }
            catch (Exception ex)
            {
                LogError($"Getting EFLOW VM info failed: {ex.Message}");
            }
        }

        private static async Task StartEflowVmInfoAsync(CancellationToken cancellationToken)
        {
            try
            {
                LogMessage("Starting EFLOW VM...");
                string script = @"Start-EflowVm";
                await ExecutePowerShellScriptAsync(script, cancellationToken);
                LogMessage("EFLOW VM started successfully.");
            }
            catch (Exception ex)
            {
                LogError($"Starting EFLOW VM failed: {ex.Message}");
            }
        }

        private static async Task EnableFirewallSettingsForEflowVm()
        {
            try
            {
                LogMessage("Enabling Firewall for EFLOW VM...");
                string script = @"Invoke-EflowVmCommand 'sudo iptables -A INPUT -p icmp -j ACCEPT'";
                await ExecutePowerShellScriptAsync(script, CancellationToken.None);
                LogMessage("Firewall enabled successfully on EFLOW VM.");
            }
            catch (Exception ex)
            {
                LogError($"Starting EFLOW VM failed: {ex.Message}");
            }
        }

        private static string GenerateDeviceRegistrationID(string deviceName)
        {
            if (string.IsNullOrWhiteSpace(deviceName))
            {
                throw new ArgumentException("Device name cannot be empty or whitespace", nameof(deviceName));
            }

            // Remove invalid characters and spaces
            var sanitizedName = Regex.Replace(deviceName, @"[^\w\-]", "");
            LogMessage($"Generating Registration ID based on device name {deviceName}...");
            var registrationId = sanitizedName;
            LogMessage($"Generated Registration ID: {registrationId}");
            return registrationId;
        }

        private static string ComputeKeyHash(string enrollmentKey, string deviceId)
        {
            using var hmac = new HMACSHA256(Convert.FromBase64String(enrollmentKey));
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(deviceId)));
        }

        private static void LogMessage(string message, Dictionary<string, object>? properties = null)
        {
            var timestampedMessage = $"InstallationCommonApp - {DateTime.Now:yyyy-MM-dd HH:mm:ss} - {message}";
            if (properties != null && logger != null)
            {
                using (logger.BeginScope(properties))
                {
                    logger.LogInformation(timestampedMessage);
                }
            }
            else
            {
                logger?.LogInformation(timestampedMessage);
            }
            File.AppendAllText(logFilePath, timestampedMessage + Environment.NewLine);
        }

        private static void LogError(string message)
        {
            string timestampedMessage = $"InstallationCommonApp - {DateTime.Now:yyyy-MM-dd HH:mm:ss} - ERROR: {message}";
            logger?.LogError(timestampedMessage); // Use logger here
            File.AppendAllText(logFilePath, timestampedMessage + Environment.NewLine);
        }

        private static async Task CleanupInstallationFiles()
        {
            try
            {
                string msiPath = Path.Combine(Path.GetTempPath(), "AzureIoTEdge.msi");
                if (File.Exists(msiPath))
                {
                    await Task.Run(() => File.Delete(msiPath));
                }
            }
            catch (Exception ex)
            {
                LogError($"Failed to cleanup installation files: {ex.Message}");
            }
        }

        // Add new method to configure EFLOW DNS
        private static async Task<bool> ConfigureEflowDNSAsync()
        {
            try
            {
                LogMessage("Configuring EFLOW DNS settings...");

                // Get EFLOW endpoint name
                string script = "Get-EflowVmEndpoint | Select-Object -ExpandProperty Name";
                string endpointName = await ExecutePowerShellScriptWithOutputAsync(script, CancellationToken.None);

                if (string.IsNullOrEmpty(endpointName))
                {
                    LogError("Failed to get EFLOW endpoint name");
                    return false;
                }

                // Configure DNS servers
                script = $@"Set-EflowVmDNSServers -vendpointName '{endpointName.Trim()}' -dnsServers @('8.8.8.8', '8.8.4.4')";
                await ExecutePowerShellScriptAsync(script, CancellationToken.None);

                // Restart EFLOW VM using Stop and Start commands
                LogMessage("Restarting EFLOW VM...");
                await ExecutePowerShellScriptAsync("Stop-EflowVm", CancellationToken.None);
                await Task.Delay(TimeSpan.FromSeconds(10)); // Wait for VM to stop
                await ExecutePowerShellScriptAsync("Start-EflowVm", CancellationToken.None);
                await Task.Delay(TimeSpan.FromSeconds(60)); // Wait for VM to start and services to initialize

                LogMessage("EFLOW DNS configuration completed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                LogError($"EFLOW DNS configuration failed: {ex.Message}");
                return false;
            }
        }

        private class InstallationState
        {
            public bool VmSwitchCreated { get; set; }
            public bool DnsConfigured { get; set; }
            public bool IoTEdgeInstalled { get; set; }
            public string? RegistrationId { get; set; }
        }

        private static async Task RollbackInstallation(InstallationState state)
        {
            LogMessage("Starting installation rollback...");

            try
            {

                if (state.IoTEdgeInstalled)
                {
                    await UninstallIoTEdgeAsync();
                }

                if (isServerOS && state.VmSwitchCreated)
                {
                    await RemoveVMSwitchAsync();
                }
                else if (!isServerOS && state.DnsConfigured)
                {
                    LogMessage("Note: DNS settings were modified during installation. You may need to manually restore your original DNS settings.");
                }

                LogMessage("Rollback completed successfully.");
            }
            catch (Exception ex)
            {
                LogError($"Error during rollback: {ex.Message}");
                LogMessage("Manual cleanup may be required. Please follow these steps:");
                LogMessage("1. Uninstall 'IoT Edge Installer' from Programs and Features");
                LogMessage("2. Remove the VM Switch if created");
                LogMessage("3. Check and restore DNS settings if modified");
            }
        }

        private static async Task UninstallIoTEdgeAsync()
        {
            try
            {
                LogMessage("Checking for Azure IoT Edge installation...");
                if (!OperatingSystem.IsWindows())
                {
                    LogMessage("Skipping uninstallation - not supported on this platform.");
                    return;
                }

                await Task.Run(() =>
                {
#if WINDOWS
                    using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Product WHERE Name = 'Azure IoT Edge LTS'");
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        LogMessage("Found Azure IoT Edge installation. Uninstalling...");
                        var result = obj.InvokeMethod("Uninstall", null);
                        
                        if (result != null && (uint)result == 0)
                        {
                            LogMessage("Azure IoT Edge uninstalled successfully.");
                        }
                        else
                        {
                            LogError($"Failed to uninstall Azure IoT Edge. Return code: {result}");
                        }
                    }
#endif
                });
            }
            catch (Exception ex)
            {
                LogError($"Error uninstalling Azure IoT Edge: {ex.Message}");
                throw;
            }
        }

        private static async Task RemoveVMSwitchAsync()
        {
            try
            {
                LogMessage("Removing VM Switch...");
                string script = $"Remove-VMSwitch -Name '{vmSwitchName}' -Force";
                await ExecutePowerShellScriptAsync(script, CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogError($"Error removing VM Switch: {ex.Message}");
            }
        }

        private static void DeregisterDeviceAsync(string? registrationId)
        {
            if (string.IsNullOrEmpty(registrationId))
                return;

            try
            {
                LogMessage($"Note: Cannot fully deprovision device {registrationId} from DPS as this requires service-level access.");
                LogMessage("Device deprovisioning must be performed from the service side using the DPS service credentials.");
                LogMessage("Performing local cleanup only...");
            }
            catch (Exception ex)
            {
                LogError($"Error during local cleanup: {ex.Message}");
            }
        }

        private static bool ValidateConfiguration()
        {
            var requiredConfigs = new[] { "DpsScopeId", "PrimaryKey", "SecondaryKey" };
            foreach (var config in requiredConfigs)
            {
                if (string.IsNullOrEmpty(Configuration.Config[config]))
                {
                    LogError($"Missing required configuration: {config}");
                    return false;
                }
            }
            return true;
        }

        private static async Task<bool> ValidatePrerequisites(CancellationToken cancellationToken)
        {
            if (!OperatingSystem.IsWindows())
            {
                LogError(ErrorMessages.WindowsRequired);
                return false;
            }

            if (!IsAdministrator())
            {
                LogError(ErrorMessages.AdminRequired);
                return false;
            }

            if (!ValidateConfiguration())
            {
                return false;
            }

            if (!ValidateSystemRequirements())
            {
                return false;
            }

            if (!ValidateDiskSpace(AppDomain.CurrentDomain.BaseDirectory))
            {
                return false;
            }

            if (!await CheckConnectivity(cancellationToken))
            {
                LogError("Required network endpoints are not accessible. Please check your network connection and try again.");
                return false;
            }

            return true;
        }

        // Add a generic retry helper
        private static async Task<T> RetryWithBackoff<T>(Func<Task<T>> operation, CancellationToken cancellationToken, int maxAttempts = 3, int initialDelayMs = 1000)
        {
            for (int i = 1; i <= maxAttempts; i++)
            {
                try
                {
                    return await operation();
                }
                catch (Exception ex) when (i < maxAttempts)
                {
                    var delay = initialDelayMs * Math.Pow(2, i - 1);
                    LogMessage($"Attempt {i} failed: {ex.Message}. Retrying in {delay}ms...");
                    await Task.Delay((int)delay, cancellationToken);
                }
            }
            throw new Exception($"Operation failed after {maxAttempts} attempts");
        }

        private static async Task<bool> CheckConnectivity(CancellationToken cancellationToken)
        {
            var endpoints = new[]
            {
                "aka.ms", // For MSI download
                // Add other required endpoints
            };

            foreach (var endpoint in endpoints)
            {
                var success = await RetryWithBackoff(async () =>
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromSeconds(10);
                    var response = await client.GetAsync($"https://{endpoint}", cancellationToken);
                    return response.IsSuccessStatusCode;
                }, cancellationToken);

                if (!success)
                {
                    LogError($"Cannot reach {endpoint} after multiple attempts");
                    return false;
                }
            }
            return true;
        }

        private static class ErrorMessages
        {
            public const string AdminRequired = "This application must be run as Administrator. Right-click and select 'Run as Administrator'.";
            public const string WindowsRequired = "This application is only supported on Windows operating systems.";
            public const string InstallationFailed = "The installation encountered an error and could not be completed successfully. A partial installation has occurred. Please uninstall the instance named 'IoT Edge Installer' and try the installation again.";
            // Add more standardized error messages
        }

        private static bool ValidateDiskSpace(string installPath, long requiredSpaceInBytes = 1024 * 1024 * 1024) // 1GB
        {
            try
            {
                var rootPath = Path.GetPathRoot(installPath) ?? throw new InvalidOperationException("Could not determine drive root path");
                var driveInfo = new DriveInfo(rootPath);
                if (driveInfo.AvailableFreeSpace < requiredSpaceInBytes)
                {
                    LogError($"Insufficient disk space. Required: {requiredSpaceInBytes / (1024 * 1024)}MB, Available: {driveInfo.AvailableFreeSpace / (1024 * 1024)}MB");
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to check disk space: {ex.Message}");
                return false;
            }
        }

        private class InstallationException : Exception
        {
            public InstallationException(string message, Exception? innerException = null)
                : base(message, innerException) { }
        }

        private static async Task HandleInstallationError(Exception ex)
        {
            LogError($"Installation failed: {ex.Message}");
            if (ex.InnerException != null)
            {
                LogError($"Caused by: {ex.InnerException.Message}");
            }
            await RollbackInstallation(new InstallationState());
        }

        private static class InstallationConstants
        {
            public const int MinimumDiskSpaceMB = 1024; // 1GB
            public const int MaxRetryAttempts = 3;
            public const int NetworkTimeoutSeconds = 10;
            public const int DownloadTimeoutMinutes = 10;
            public const string InstallerFileName = "AzureIoTEdge.msi";
        }

        public class InstallationProgressEventArgs : EventArgs
        {
            public int ProgressPercentage { get; set; }
            public required string StatusMessage { get; set; }
            public string? DetailedMessage { get; set; }
        }

        public static event EventHandler<InstallationProgressEventArgs>? InstallationProgress;

        private static void ReportProgress(int progress, string status, string? details = null)
        {
            InstallationProgress?.Invoke(null, new InstallationProgressEventArgs
            {
                ProgressPercentage = progress,
                StatusMessage = status,
                DetailedMessage = details
            });
        }

        private static bool ValidateSystemRequirements()
        {
            try
            {
                // Check CPU cores
                var processorCount = Environment.ProcessorCount;
                if (processorCount < 2)
                {
                    LogError("Minimum of 2 CPU cores required");
                    return false;
                }

                // Check total RAM - using different method
                var memoryInfo = GC.GetGCMemoryInfo();
                var totalAvailableMemoryBytes = memoryInfo.TotalAvailableMemoryBytes;
                if (totalAvailableMemoryBytes < 2L * 1024 * 1024 * 1024) // 2GB in bytes
                {
                    LogError("Minimum of 2GB RAM required");
                    return false;
                }

                return true;
            }
            catch (Exception ex)
            {
                LogError($"Failed to validate system requirements: {ex.Message}");
                return false;
            }
        }

        private static void ValidateFilePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("Path cannot be empty");

            if (!Path.GetFullPath(path).StartsWith(AppDomain.CurrentDomain.BaseDirectory, StringComparison.OrdinalIgnoreCase))
                throw new SecurityException("Access to paths outside installation directory is not allowed");
        }

        private static void WriteParamsSecurely(string filePath, string content)
        {
            var fileInfo = new FileInfo(filePath);
            using (var fs = fileInfo.Create())
            using (var sw = new StreamWriter(fs))
            {
                sw.Write(content);
            }

            // Set Windows-specific permissions
            if (OperatingSystem.IsWindows())
            {
                var security = fileInfo.GetAccessControl();
                security.SetAccessRuleProtection(true, false);
                fileInfo.SetAccessControl(security);
            }
        }
    }
}
