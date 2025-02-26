$hyperVFeature = Get-WindowsOptionalFeature -FeatureName Microsoft-Hyper-V-All -Online

if ($hyperVFeature.State -eq "Enabled") {
    Write-Output "Hyper-V is already enabled."
    exit 0
} else {
    Write-Output "Hyper-V is not enabled. Enabling Hyper-V..."
    Enable-WindowsOptionalFeature -Online -FeatureName Microsoft-Hyper-V-All -NoRestart
    exit 3010  # 3010 is the exit code for a successful operation that requires a restart
}
