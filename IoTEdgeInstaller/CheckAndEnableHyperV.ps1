# Function to check if running as admin
function Test-Admin {
    return ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Function to get the host OS type
function Get-HostOSType {
    $osInfo = Get-CimInstance -ClassName Win32_OperatingSystem
    $osCaption = $osInfo.Caption

    if ($osCaption -like "*Server*") {
        return "Server"
    } else {
        return "Client"
    }
}

# Function to enable Hyper-V on Windows Server
function Enable-HyperVServer {
    Write-Output "Operating system is Windows Server."
    $hypervFeature = Get-WindowsFeature -Name Hyper-V
    $rsatHypervTools = Get-WindowsFeature -Name RSAT-Hyper-V-Tools

    if ($hypervFeature.Installed -and $rsatHypervTools.Installed) {
        Write-Output "Hyper-V and RSAT-Hyper-V-Tools are already enabled."
        exit 0
    } elseif ($hypervFeature.Installed) {
        Write-Output "Hyper-V is already enabled, but RSAT-Hyper-V-Tools is not. Enabling RSAT-Hyper-V-Tools..."
        $rsatInstallResult = Install-WindowsFeature -Name RSAT-Hyper-V-Tools -IncludeAllSubFeature
        if ($rsatInstallResult.Success -and -not $rsatInstallResult.RestartNeeded) {
            Write-Output "RSAT-Hyper-V-Tools enabled successfully."
            exit 0
        } elseif ($rsatInstallResult.RestartNeeded) {
            Write-Output "RSAT-Hyper-V-Tools enabled successfully. A restart is required."
            exit 3010
        } else {
            Write-Output "Failed to enable RSAT-Hyper-V-Tools."
            exit 1
        }
    } else {
        Write-Output "Hyper-V is not enabled. Enabling Hyper-V and RSAT-Hyper-V-Tools..."
        $hypervInstallResult = Install-WindowsFeature -Name Hyper-V -IncludeAllSubFeature
        $rsatInstallResult = Install-WindowsFeature -Name RSAT-Hyper-V-Tools -IncludeAllSubFeature
        if ($hypervInstallResult.Success -and $rsatInstallResult.Success -and -not $hypervInstallResult.RestartNeeded -and -not $rsatInstallResult.RestartNeeded) {
            Write-Output "Hyper-V and RSAT-Hyper-V-Tools enabled successfully."
            exit 0
        } elseif ($hypervInstallResult.RestartNeeded -or $rsatInstallResult.RestartNeeded) {
            Write-Output "Hyper-V and RSAT-Hyper-V-Tools enabled successfully. A restart is required."
            exit 3010
        } else {
            Write-Output "Failed to enable Hyper-V and RSAT-Hyper-V-Tools."
            exit 1
        }
    }
}

# Function to enable Hyper-V on Windows Client
function Enable-HyperVClient {
    Write-Output "Operating system is Windows 10 or 11."
    $hypervFeature = Get-WindowsOptionalFeature -FeatureName Microsoft-Hyper-V-All -Online
    if ($hypervFeature.State -eq "Enabled") {
        Write-Output "Hyper-V is already enabled."
        exit 0
    } else {
        Write-Output "Hyper-V is not enabled. Enabling Hyper-V..."
        Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -NoRestart
        if ($?) {   
            Write-Output "Hyper-V enabled successfully. A restart is required."
            exit 3010  # 3010 is the exit code for a successful operation that requires a restart
        } else {
            Write-Output "Failed to enable Hyper-V."
            exit 1
        }
    }
}

# Relaunch as admin if not already
if (-not (Test-Admin)) {
    Write-Output "Relaunching script as administrator..."
    Start-Process powershell "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

# Enable error handling
$ErrorActionPreference = "Stop"

# Check the operating system
$hostOSType = Get-HostOSType

# Enable Hyper-V
if ($hostOSType -eq "Server") {
    Enable-HyperVServer
} else {
    Enable-HyperVClient
}
