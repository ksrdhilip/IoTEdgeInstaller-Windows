# IoT Edge Installer for Windows
A flexible and reusable installer for deploying Azure IoT Edge on Windows systems, integrated with Azure Device Provisioning Service (DPS) for automatic device provisioning and configuration in IoT Hub. This project automates the installation of the IoT Edge runtime, provisions devices using symmetric key attestation, and ensures proper Hyper-V configuration for running the EFLOW (Edge for Linux on Windows) virtual machine. Designed for both Windows Client (10/11) and Windows Server (2019/2022), it includes prerequisite checks, error handling, logging, and rollback capabilities, making it adaptable for various IoT Edge deployments.

## Features:

Automated IoT Edge Deployment: Installs Azure IoT Edge LTS runtime (x64/ARM64) and deploys the EFLOW VM.
DPS Integration: Registers devices in IoT Hub using symmetric key attestation based on a user-provided identifier (e.g., store name).
Hyper-V Management: Enables Hyper-V and configures virtual switches for Windows Server environments.
Prerequisite Validation: Ensures admin rights, compatible OS, sufficient resources (CPU, RAM, disk), and network connectivity.
Post-Reboot Continuation: Schedules tasks to resume installation after reboot if Hyper-V enabling requires it.
Error Handling & Rollback: Provides detailed logging and cleanup on failure with user guidance.
Configurable: Uses `appsettings.json` for DPS credentials and other settings.

## Supported Platforms:

Windows 10/11 (Version 17763 or higher)
Windows Server 2019/2022

## Prerequisites:
.NET 8.0 Runtime
PowerShell 5.1 or higher
Administrative privileges
Internet access for MSI download and DPS communication

## Usage:

1. Clone the repository: `git clone https://github.com/yourusername/IoTEdgeInstaller.git`
2. Open `IoTEdgeInstallerSolution.sln` in Visual Studio 2022 (Version 17.9+ recommended).
3. Update `InstallationCommonApp/appsettings.json` with your DPS Scope ID, Primary Key, and Secondary Key.
4. Build and run `IoTEdgeInstaller` as Administrator.
5. Enter a unique identifier (e.g., device name) when prompted to generate a device registration ID.

## Project Structure:

- `IoTEdgeInstaller`: Entry point for initial setup, Hyper-V enabling, and prerequisite checks.
- `InstallationCommonApp`: Core logic for IoT Edge installation, EFLOW deployment, and DPS provisioning.
- `PostRebootInstallerService`: Handles installation continuation after a required reboot.
- `CommonUtilities`: Shared utilities (e.g., logging, process helpers).
- `Solution Items`: Documentation and demo GIFs.

## Contributing:

Fork the repository, create a feature branch, and submit a pull request. See `CONTRIBUTING.md` for details (to be added).

## License:

`MIT License` - free to use, modify, and distribute.
