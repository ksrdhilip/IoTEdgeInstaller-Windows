namespace CommonUtilities
{
    public static class ProgressHelper
    {
        public static void ReportProgress(int percentComplete, string status)
        {
            Console.WriteLine($"Progress: {percentComplete}% - {status}");
        }
    }
}