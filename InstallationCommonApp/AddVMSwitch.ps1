# Function to check if running as admin
function Test-Admin {
    return ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

# Relaunch as admin if not already
if (-not (Test-Admin)) {
    Write-Output "Relaunching script as administrator..."
    Start-Process powershell "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs
    exit
}

# Enable error handling
$ErrorActionPreference = "Stop"

# Variables for the setup
$vmSwitchName = "IoTEdgeVSwitch"
$vmSwitchIpAddress = "192.168.3.1"
$eflowVmIpAddress = "192.168.3.5"
$vmSubnet = "192.168.3.0/24"
$natName = "IoTEdgeVSwitchNat"
$eflowVmPrefixLength = 24

# Function to find an available IP address in the specified range
function Get-AvailableIPAddress {
    param (
        [string]$preferredIpAddress,
        [string]$baseIpAddress
    )
    
    # Check if the preferred IP address is available
    try {
        $pingResult = Test-Connection -ComputerName $preferredIpAddress -Count 1 -ErrorAction Stop
        if (-not $pingResult.StatusCode) {
            return $preferredIpAddress
        }
    } catch {
        #Write-Output "$preferredIpAddress is available."
        return $preferredIpAddress
    }
    
    # If the preferred IP address is not available, find the next available IP address
    $ipParts = $baseIpAddress.Split('.')
    $baseIpPrefix = "$($ipParts[0]).$($ipParts[1]).$($ipParts[2])."
    $startingIp = [int]$ipParts[3]
    for ($i = $startingIp; $i -le 254; $i++) {
        $ipAddress = $baseIpPrefix + $i
        try {
            $pingResult = Test-Connection -ComputerName $ipAddress -Count 1 -ErrorAction Stop
            if (-not $pingResult.StatusCode) {
                return $ipAddress
            }
        } catch {
            Write-Output "Ping to IP address $ipAddress failed: $_"
        }
    }
    return $null
}

try {
    # Step 1: Check if the VMSwitch already exists
    $vmswitch = Get-VMSwitch -Name $vmSwitchName -ErrorAction SilentlyContinue
    if ($null -ne $vmswitch) {
        Write-Output "VMSwitch '$vmSwitchName' already exists."
    } else {
        # Create a new VMSwitch
        New-VMSwitch -Name $vmSwitchName -SwitchType Internal -ErrorAction Stop
        Write-Output "VMSwitch '$vmSwitchName' created."
    }

    # Step 2: Assign the IP address to the VMSwitch
    $ipaddress = Get-NetIPAddress -IPAddress $vmSwitchIpAddress -ErrorAction SilentlyContinue
    if ($null -ne $ipaddress) {
        Write-Output "IP address '$vmSwitchIpAddress' is already assigned to the VMSwitch."
    } else {
        $ifIndex = (Get-NetAdapter -Name "vEthernet ($vmSwitchName)").ifIndex
        New-NetIPAddress -IPAddress $vmSwitchIpAddress -PrefixLength $eflowVmPrefixLength -InterfaceIndex $ifIndex -ErrorAction Stop
        Write-Output "Assigned IP address '$vmSwitchIpAddress' to '$vmSwitchName'."
    }

    # Step 3: Configure NAT for the VMSwitch
    Write-Output "Get-NetNat -Name ""$natName"""
    $nat = Get-NetNat -Name "$natName" -ErrorAction SilentlyContinue
    Write-Output "Nat Name:"
    Write-Output "Nat Name: $nat"
    if ($null -eq $nat) {
        Write-Output "Adding New NAT '$natName'"
        New-NetNat -Name $natName -InternalIPInterfaceAddressPrefix $vmSubnet -ErrorAction $ErrorActionPreference
        Write-Output "NAT '$natName' configured for subnet '$vmSubnet'."
    } else {
        Write-Output "NAT '$natName' already exists."
    }

    # Step 4: Enable IP forwarding on the VMSwitch
    $ipInterface = Get-NetIPInterface -InterfaceAlias "vEthernet ($vmSwitchName)" -ErrorAction $ErrorActionPreference
    if ($ipInterface.Forwarding -eq "Enabled") {
        Write-Output "IP forwarding is already enabled on '$vmSwitchName'."
    } else {
        Set-NetIPInterface -InterfaceAlias "vEthernet ($vmSwitchName)" -Forwarding Enabled -ErrorAction $ErrorActionPreference
        Write-Output "IP forwarding enabled on '$vmSwitchName'."
    }

    # Step 5: Find an available IP address for EFLOW VM
    $assignedIpAddress = Get-AvailableIPAddress -preferredIpAddress $eflowVmIpAddress -baseIpAddress $eflowVmIpAddress
    if ($null -eq $assignedIpAddress) {
        Write-Output "No available IP addresses found in the range 192.168.3.5 to 192.168.3.254."
        exit 1
    } else {
        Write-Output "Assigned IP address '$assignedIpAddress' for EFLOW VM installation."
    }

    Write-Output "Setup complete. VMSwitchName: $vmSwitchName, EFLOWVMIP: $assignedIpAddress, GatewayIP: $vmSwitchIpAddress, EFLOWVMIPV4PrefixLength: $eflowVmPrefixLength"
} catch {
    Write-Output "An error occurred: $_"
    exit 1
}
