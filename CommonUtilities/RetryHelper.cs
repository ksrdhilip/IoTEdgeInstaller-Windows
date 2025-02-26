namespace CommonUtilities
{
    public static class RetryHelper
    {
        public static async Task<T> RetryWithBackoff<T>(Func<Task<T>> operation, CancellationToken cancellationToken, int maxRetries = 3)
        {
            int retryCount = 0;
            while (true)
            {
                try
                {
                    return await operation();
                }
                catch (Exception) when (retryCount < maxRetries)
                {
                    retryCount++;
                    await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, retryCount)), cancellationToken);
                }
            }
        }
    }
}