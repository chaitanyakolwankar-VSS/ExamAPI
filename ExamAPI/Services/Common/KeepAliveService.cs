namespace ExamAPI.Services.Common
{
    /// <summary>
    /// Keeps IIS from stopping the API after its idle timeout (20 minutes by default): every 10 minutes the
    /// API calls its own public health check (<c>KeepAlive:Url</c>), which IIS counts as activity. Off when the
    /// setting is empty. Only that one configured address is ever called, and only GET /api/health.
    /// </summary>
    public sealed class KeepAliveService(IConfiguration configuration, IHttpClientFactory httpClientFactory, ILogger<KeepAliveService> logger)
        : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(10);

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var url = configuration["KeepAlive:Url"];
            if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
                return;

            var client = httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            var failedBefore = false;
            using var timer = new PeriodicTimer(Interval);
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var response = await client.GetAsync(uri, stoppingToken);
                    if (!response.IsSuccessStatusCode && !failedBefore)
                        logger.LogWarning("Keep-alive: {Url} answered {Status}.", uri, (int)response.StatusCode);
                    failedBefore = !response.IsSuccessStatusCode;
                }
                catch (Exception ex) when (ex is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
                {
                    // Logged once per run of failures, so a wrong address does not fill the log.
                    if (!failedBefore)
                        logger.LogWarning(ex, "Keep-alive: {Url} could not be reached; check KeepAlive:Url.", uri);
                    failedBefore = true;
                }
            }
        }
    }
}
