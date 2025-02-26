#!/bin/bash

LOGFILE="/var/log/iotedge_install_provision.log"
exec > >(tee -i $LOGFILE)
exec 2>&1

echo "Starting IoT Edge installation and provisioning..."

# Function to check disk space
check_disk_space() {
    required_space_mb=500 # Minimum required disk space in MB
    available_space_mb=$(df / | tail -1 | awk '{print $4}')

    if (( available_space_mb < required_space_mb * 1024 )); then
        echo "Error: Not enough disk space. At least $required_space_mb MB is required."
        exit 1
    else
        echo "Disk space check passed. Available space: $(( available_space_mb / 1024 )) MB."
    fi
}

# Function to check physical memory
check_physical_memory() {
    required_memory_mb=512 # Minimum required memory in MB
    available_memory_kb=$(grep MemTotal /proc/meminfo | awk '{print $2}')

    if (( available_memory_kb < required_memory_mb * 1024 )); then
        echo "Error: Not enough physical memory. At least $required_memory_mb MB is required."
        exit 1
    else
        echo "Physical memory check passed. Available memory: $(( available_memory_kb / 1024 )) MB."
    fi
}

# Function to check for existing IoT Edge installation
check_existing_installation() {
    if command -v iotedge &> /dev/null; then
        echo "IoT Edge runtime is already installed."
        read -p "Do you want to re-install IoT Edge runtime? (yes/no): " choice
        if [[ "$choice" != "yes" ]]; then
            echo "Exiting installation as per user choice."
            exit 1
        fi
    fi
}

# Function to install IoT Edge for RHEL
install_iot_edge_rhel() {
    local version=$1
    echo "Installing IoT Edge runtime on $os, version: $version"
    if [[ $version == "9.x" ]]; then
        echo "Adding Microsoft package repository for RHEL 9.x..."
        wget https://packages.microsoft.com/config/rhel/9.0/packages-microsoft-prod.rpm -O packages-microsoft-prod.rpm
        sudo yum localinstall packages-microsoft-prod.rpm -y
        rm packages-microsoft-prod.rpm
    elif [[ $version == "8.x" ]]; then
        echo "Adding Microsoft package repository for RHEL 8.x..."
        wget https://packages.microsoft.com/config/rhel/8/packages-microsoft-prod.rpm -O packages-microsoft-prod.rpm
        sudo yum localinstall packages-microsoft-prod.rpm -y
        rm packages-microsoft-prod.rpm
    fi

    echo "Updating package list for RHEL $version..."
    sudo yum update -y

    echo "Installing Moby engine and CLI for RHEL $version..."
    sudo yum install moby-engine moby-cli -y

    echo "Installing IoT Edge runtime for RHEL $version..."
    sudo yum install aziot-edge -y

    echo "Checking IoT Edge installation..."
    if ! yum list installed aziot-edge; then
        echo "IoT Edge installation failed."
        exit 1
    fi
}

# Function to install IoT Edge for Ubuntu
install_iot_edge_ubuntu() {
    local version=$1
    echo "Installing IoT Edge runtime on $os, version: $version"
    if [[ $version == "22.04" ]]; then
        echo "Adding Microsoft package repository for Ubuntu 22.04..."
        wget https://packages.microsoft.com/config/ubuntu/22.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
    elif [[ $version == "20.04" ]]; then
        echo "Adding Microsoft package repository for Ubuntu 20.04..."
        wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
    fi

    sudo dpkg -i packages-microsoft-prod.deb
    rm packages-microsoft-prod.deb

    echo "Updating package list for Ubuntu $version..."
    sudo apt-get update

    echo "Installing Moby engine and CLI for Ubuntu $version..."
    sudo apt-get install moby-engine -y

    echo "Installing IoT Edge runtime for Ubuntu $version..."
    if [[ $version == "22.04" ]]; then
        sudo apt-get install aziot-edge -y
    elif [[ $version == "20.04" ]]; then
        sudo apt-get install aziot-edge defender-iot-micro-agent-edge -y
    fi

    echo "Checking IoT Edge installation..."
    if ! dpkg -l | grep aziot-edge; then
        echo "IoT Edge installation failed."
        exit 1
    fi
}

# Function to install IoT Edge for Debian
install_iot_edge_debian() {
    echo "Installing IoT Edge runtime on $os, version: $version"
    echo "Adding Microsoft package repository for Debian 11..."
    wget https://packages.microsoft.com/config/debian/11/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
    sudo apt install ./packages-microsoft-prod.deb
    rm packages-microsoft-prod.deb

    echo "Updating package list for Debian 11..."
    sudo apt-get update

    echo "Installing Moby engine and CLI for Debian 11..."
    sudo apt-get install moby-engine -y

    echo "Installing IoT Edge runtime for Debian 11..."
    sudo apt-get install aziot-edge defender-iot-micro-agent-edge -y

    echo "Checking IoT Edge installation..."
    if ! dpkg -l | grep aziot-edge; then
        echo "IoT Edge installation failed."
        exit 1
    fi
}

# Function to check and enable IoT Edge service
check_and_enable_service() {
    echo "Checking for IoT Edge service files in common directories..."
    if [ -f /lib/systemd/system/iotedge.service ] || [ -f /etc/systemd/system/iotedge.service ] || [ -f /usr/lib/systemd/system/iotedge.service ]; then
        echo "IoT Edge service files found."

        echo "Enabling and starting IoT Edge service..."
        sudo systemctl daemon-reload
        sudo systemctl enable iotedge
        sudo systemctl start iotedge
        sudo systemctl status iotedge

        echo "IoT Edge installation and service setup completed."
    else
        echo "IoT Edge service files not found in common directories. Installation may have failed."
        exit 1
    fi
}

# Detect OS and version
os=$(awk -F= '/^NAME/{print $2}' /etc/os-release)
version=$(awk -F= '/^VERSION_ID/{print $2}' /etc/os-release)

# Perform installation and configuration based on OS and version
echo "Detected OS: $os, Version: $version"

# Check system resources and existing installation
check_disk_space
check_physical_memory
check_existing_installation

if [[ $os == *"Red Hat"* ]]; then
    if [[ $version == *"9."* ]]; then
        install_iot_edge_rhel "9.x"
        ./provision_iotedge.sh
        check_and_enable_service        
    elif [[ $version == *"8."* ]]; then
        install_iot_edge_rhel "8.x"
        ./provision_iotedge.sh
        check_and_enable_service
    else
        echo "Unsupported Red Hat version: $version"
        exit 1
    fi
elif [[ $os == *"Ubuntu"* ]]; then
    if [[ $version == "22.04" || $version == "20.04" ]]; then
        install_iot_edge_ubuntu "$version"
        ./provision_iotedge.sh
        check_and_enable_service
    else
        echo "Unsupported Ubuntu version: $version"
        exit 1
    fi
elif [[ $os == *"Debian"* ]]; then
    if [[ $version == "11" ]]; then
        install_iot_edge_debian
        ./provision_iotedge.sh
        check_and_enable_service
    else
        echo "Unsupported Debian version: $version"
        exit 1
    fi
else
    echo "Unsupported OS: $os"
    exit 1
fi

echo "IoT Edge installation and provisioning completed."

# Instructions for the user
echo "Once the IoT Edge service starts, it will connect to DPS, which will then register the device in the IoT Hub based on the Enrollment Group settings."
