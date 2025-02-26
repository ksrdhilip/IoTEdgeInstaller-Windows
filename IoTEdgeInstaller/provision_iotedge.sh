#!/bin/bash

LOGFILE="/var/log/iotedge_install_provision.log"
exec > >(tee -i $LOGFILE)
exec 2>&1

# Function to generate the derived symmetric key
generate_derived_key() {
    local enrollment_key=$1
    local registration_id=$2

    # Convert the key from Base64, compute HMAC SHA256, and convert result back to Base64
    echo "$enrollment_key" | openssl base64 -d -A -out /tmp/key.bin
    local derived_key=$(echo -n "$registration_id" | openssl dgst -binary -sha256 -mac HMAC -macopt hexkey:$(xxd -p -c 256 /tmp/key.bin) | openssl base64 -A)

    echo "$derived_key"
}

# Check if already provisioned
if [ -f /etc/aziot/config.toml ]; then
    echo "Existing provisioning configuration found. Re-provisioning with new details."
    sudo mv /etc/aziot/config.toml /etc/aziot/config.toml.bak
fi

# Replace with your actual DPS Scope ID and Enrollment Key
DPS_SCOPE_ID="PASTE_YOUR_DPS_SCOPEID_HERE"
ENROLLMENT_KEY="PASTE_YOUR_ENROLLMENT_GROUP_PRIMARY_KEY_HERE"

# Prompt the user to enter the hostname
read -p "Enter the hostname to use for registration: " HOSTNAME

# Generate the derived symmetric key
GROUP_SYMMETRIC_KEY=$(generate_derived_key "$ENROLLMENT_KEY" "$HOSTNAME")
echo "Derived symmetric key: $GROUP_SYMMETRIC_KEY"

# Write the new configuration to config.toml
echo "Writing DPS configuration to /etc/aziot/config.toml..."
sudo tee /etc/aziot/config.toml > /dev/null <<EOL
# DPS provisioning with symmetric key
[provisioning]
source = "dps"
global_endpoint = "https://global.azure-devices-provisioning.net"
id_scope = "$DPS_SCOPE_ID"

[provisioning.attestation]
method = "symmetric_key"
registration_id = "$HOSTNAME"
symmetric_key = { value = "$GROUP_SYMMETRIC_KEY" }

# auto_reprovisioning_mode = Dynamic
EOL

echo "Applying IoT Edge configuration..."
sudo iotedge config apply

echo "DPS provisioning configuration completed."
