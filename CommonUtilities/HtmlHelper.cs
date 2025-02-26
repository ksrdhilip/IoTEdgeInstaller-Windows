using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace CommonUtilities
{
    public static class HtmlHelper
    {
        public static void CreateAndOpenSuccessHtml(string ipAddressOfEflowVm, string appName, ILogger logger)
        {
            string htmlContent = $@"
<!DOCTYPE html>
<html lang='en'>
<head>
    <meta charset='UTF-8'>
    <meta name='viewport' content='width=device-width, initial-scale=1.0'>
    <title>Installation Success</title>
    <style>
        body {{
            font-family: Arial, sans-serif;
            display: flex;
            justify-content: center;
            align-items: center;
            height: 100vh;
            background-color: #f0f8ff;
        }}
        .container {{
            text-align: center;
            padding: 20px;
            border: 2px solid #4caf50;
            border-radius: 10px;
            background-color: #ffffff;
        }}
        .success-icon {{
            font-size: 50px;
            color: #4caf50;
        }}
        .message {{
            font-size: 24px;
            margin-top: 10px;
        }}
        .instructions {{
            text-align: left;
            margin-top: 20px;
        }}
        .instructions .section {{
            margin-top: 20px;
        }}
        .instructions code {{
            display: block;
            background-color: #f4f4f4;
            padding: 10px;
            border-radius: 5px;
            margin-top: 10px;
            position: relative;
        }}
        .copy-button {{
            position: absolute;
            top: 10px;
            right: 10px;
            padding: 5px 10px;
            border: none;
            background-color: #4caf50;
            color: #ffffff;
            cursor: pointer;
            border-radius: 3px;
        }}
    </style>
</head>
<body>
    <div class='container'>
        <div class='success-icon'>&#10004;</div>
        <div class='message'>Installation was successful!</div>
        <div class='instructions'>
            <p>Installation of IoT Edge Runtime and provisioning completed successfully.</p>
            <div class='section'>
                <p>To check the installation status and other details:</p>
                <p>Open a PowerShell window with Administrator access and execute the following command:</p>
                <code id='powershell-command'>Connect-EflowVm
                    <button class='copy-button' onclick='copyToClipboard(""powershell-command"")'>Copy</button>
                </code>
            </div>
            <div class='section'>
                <p>Once you logged in to the EFLOW VM, execute the below sudo commands to check the status and modules:</p>
                <code id='sudo-command1'>sudo iotedge list
                    <button class='copy-button' onclick='copyToClipboard(""sudo-command1"")'>Copy</button>
                </code>
                <code id='sudo-command2'>sudo iotedge system logs
                    <button class='copy-button' onclick='copyToClipboard(""sudo-command2"")'>Copy</button>
                </code>
            </div>
            <div class='section'>
                <p>Eflow VM's IP address:</p>
                <code id='ip-address'>{ipAddressOfEflowVm}
                    <button class='copy-button' onclick='copyToClipboard(""ip-address"")'>Copy</button>
                </code>				
            </div>
        </div>
    </div>

    <script>
        function copyToClipboard(elementId) {{
            var copyText = document.getElementById(elementId).childNodes[0].nodeValue.trim();
            navigator.clipboard.writeText(copyText).then(function() {{
                alert('Copied to clipboard');
            }}, function(err) {{
                console.error('Could not copy text: ', err);
            }});
        }}
    </script>
</body>
</html>";

            string filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "installation_success.html");
            File.WriteAllText(filePath, htmlContent);

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = filePath,
                    UseShellExecute = true
                });
                Logger.LogMessage(logger, appName, "Success HTML file created and opened in browser.");
            }
            catch (Exception ex)
            {
                Logger.LogError(logger, appName, $"Failed to open HTML file: {ex.Message}");
            }
        }
    }
}