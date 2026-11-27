using System.Collections.Concurrent;
using ExamAPI.Models;

namespace ExamAPI.Services.Payments
{
    /// <summary>
    /// A pretend gateway for development, tests and the demo (never in Production). "Paying" opens our own page
    /// (PaymentsController, test-gateway) with buttons for each outcome; the outcome is kept in memory and given back by
    /// <see cref="GetStatusAsync"/>, exactly as a real gateway's status call would. A restart forgets everything, so
    /// open test orders then expire.
    /// </summary>
    public sealed class TestPaymentGateway : IPaymentGateway
    {
        public const string PagePath = "api/Payments/test-gateway/";

        public enum Outcome { Success, WrongAmount, Failed, Pending }

        private sealed record Entry(decimal Amount, string ReturnUrl, Outcome? Outcome, string? TxnId);

        private readonly ConcurrentDictionary<string, Entry> _orders = new(StringComparer.Ordinal);

        public string Provider => PaymentProviders.Test;
        public string DisplayName => "Test gateway (no real money)";
        public bool NeedsMerchantId => false;
        public IReadOnlyList<GatewaySecretField> SecretFields => Array.Empty<GatewaySecretField>();
        public bool AllowedInProduction => false;

        public Task<PaymentRedirect> StartAsync(GatewayAccount account, PaymentStartRequest request, CancellationToken ct = default)
        {
            _orders[request.OrderNo] = new Entry(request.Amount, request.ReturnUrl, null, null);
            return Task.FromResult(new PaymentRedirect { Url = request.ApiBaseUrl + PagePath + Uri.EscapeDataString(request.OrderNo), Method = "GET" });
        }

        public Task<GatewayStatus> GetStatusAsync(GatewayAccount account, string orderNo, CancellationToken ct = default)
        {
            if (!_orders.TryGetValue(orderNo, out var e))
                return Task.FromResult(new GatewayStatus(GatewayState.NotFound, orderNo, null, RawStatus: "NOT_FOUND"));
            return Task.FromResult(e.Outcome switch
            {
                Outcome.Success => new GatewayStatus(GatewayState.Success, orderNo, e.Amount, e.TxnId, "TEST", "Test bank", "SUCCESS"),
                Outcome.WrongAmount => new GatewayStatus(GatewayState.Success, orderNo, e.Amount - 1, e.TxnId, "TEST", "Test bank", "SUCCESS"),
                Outcome.Failed => new GatewayStatus(GatewayState.Failed, orderNo, e.Amount, e.TxnId, "TEST", "Test bank", "FAILED", "Declined by the test bank."),
                _ => new GatewayStatus(GatewayState.Pending, orderNo, e.Amount, e.TxnId, RawStatus: "PENDING"),
            });
        }

        // ---- the pretend payment page ----

        /// <summary>The order as the test page shows it, or null when this gateway never started it.</summary>
        public (decimal Amount, string ReturnUrl)? Find(string orderNo) =>
            _orders.TryGetValue(orderNo, out var e) ? (e.Amount, e.ReturnUrl) : null;

        /// <summary>Records the button pressed on the test page; answers where to send the browser back.</summary>
        public string? Decide(string orderNo, Outcome outcome)
        {
            if (!_orders.TryGetValue(orderNo, out var e)) return null;
            _orders[orderNo] = e with { Outcome = outcome, TxnId = e.TxnId ?? "TEST" + Guid.NewGuid().ToString("N")[..12].ToUpperInvariant() };
            return e.ReturnUrl;
        }
    }
}
