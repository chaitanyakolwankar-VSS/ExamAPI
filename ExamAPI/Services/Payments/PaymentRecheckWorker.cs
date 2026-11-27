namespace ExamAPI.Services.Payments
{
    /// <summary>
    /// Background: asks the gateway again about payments still open (the student closed the tab, the bank was slow).
    /// Payments:RecheckEnabled (default true), Payments:RecheckIntervalSeconds (default 60).
    /// </summary>
    public sealed class PaymentRecheckWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopes;
        private readonly ILogger<PaymentRecheckWorker> _logger;
        private readonly bool _enabled;
        private readonly TimeSpan _interval;

        public PaymentRecheckWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<PaymentRecheckWorker> logger)
        {
            _scopes = scopes;
            _logger = logger;
            _enabled = config.GetValue("Payments:RecheckEnabled", true);
            _interval = TimeSpan.FromSeconds(Math.Clamp(config.GetValue("Payments:RecheckIntervalSeconds", 60), 15, 3600));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("Payment re-check: disabled (Payments:RecheckEnabled = false).");
                return;
            }
            const int batch = 20;
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    int checkedCount;
                    do
                    {
                        using var scope = _scopes.CreateScope();
                        checkedCount = await scope.ServiceProvider.GetRequiredService<IPaymentService>().RecheckDueAsync(batch, stoppingToken);
                    } while (checkedCount == batch && !stoppingToken.IsCancellationRequested);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Payment re-check: a run failed; trying again shortly.");
                }
                try
                {
                    await Task.Delay(_interval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
