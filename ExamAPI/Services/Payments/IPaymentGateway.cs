namespace ExamAPI.Services.Payments
{
    /// <summary>A merchant account as a gateway adapter gets it: keys already decrypted, only for this call.</summary>
    public sealed record GatewayAccount(
        Guid? Id, string Provider, string? MerchantId, IReadOnlyDictionary<string, string> Secrets, bool IsTestMode);

    public sealed record PaymentStartRequest(
        string OrderNo, decimal Amount, string Currency, string Description,
        string PayerName, string? PayerEmail, string? PayerPhone,
        // Where the gateway sends the browser back: our own return endpoint for this order.
        string ReturnUrl,
        // This API's own public address, ending in "/".
        string ApiBaseUrl);

    /// <summary>Where to send the student's browser to pay: a GET link or an auto-submitted POST form.</summary>
    public sealed class PaymentRedirect
    {
        public string Url { get; set; } = "";
        /// <summary>"GET" or "POST".</summary>
        public string Method { get; set; } = "GET";
        public Dictionary<string, string> Fields { get; set; } = new();
    }

    public enum GatewayState
    {
        Success,
        Failed,
        /// <summary>The gateway has not decided yet (bank pending, student still on the page).</summary>
        Pending,
        /// <summary>The gateway does not know the order (the student never reached it).</summary>
        NotFound,
    }

    /// <summary>The gateway's own answer to "what happened to this order?" — the only thing that can make a payment count.</summary>
    public sealed record GatewayStatus(
        GatewayState State, string? OrderNo, decimal? Amount, string? TxnId = null, string? Mode = null,
        string? Bank = null, string? RawStatus = null, string? Message = null);

    /// <summary>A key the Platform page asks for when this provider is chosen.</summary>
    public sealed record GatewaySecretField(string Key, string Label, bool IsSecret = true);

    /// <summary>
    /// One payment provider. Adding a real gateway = one class implementing this, registered in Program.cs, plus tests.
    /// Rules every adapter keeps: <see cref="GetStatusAsync"/> asks the gateway server-to-server (never trusts what the
    /// browser brings back); no key is ever logged or returned.
    /// </summary>
    public interface IPaymentGateway
    {
        /// <summary><see cref="Models.PaymentProviders"/> code.</summary>
        string Provider { get; }

        string DisplayName { get; }

        /// <summary>Whether the merchant id is needed, and which keys, for the Platform page.</summary>
        bool NeedsMerchantId { get; }
        IReadOnlyList<GatewaySecretField> SecretFields { get; }

        /// <summary>False = never offered or used in Production (the built-in test gateway).</summary>
        bool AllowedInProduction { get; }

        Task<PaymentRedirect> StartAsync(GatewayAccount account, PaymentStartRequest request, CancellationToken ct = default);

        Task<GatewayStatus> GetStatusAsync(GatewayAccount account, string orderNo, CancellationToken ct = default);
    }
}
