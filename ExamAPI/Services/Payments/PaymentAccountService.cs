using System.Text.Json;
using ExamAPI.Data;
using ExamAPI.Models;
using ExamAPI.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace ExamAPI.Services.Payments
{
    public class PaymentProviderDto
    {
        public string Provider { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool NeedsMerchantId { get; set; }
        public List<GatewaySecretField> SecretFields { get; set; } = new();
    }

    /// <summary>An account as the platform screen shows it: never a key, only which ones are saved.</summary>
    public class PaymentAccountView
    {
        public bool Exists { get; set; }
        public string? Provider { get; set; }
        public string? MerchantId { get; set; }
        public bool IsTestMode { get; set; } = true;
        public bool IsEnabled { get; set; } = true;
        /// <summary>Secret field keys that have a saved value.</summary>
        public List<string> SavedSecrets { get; set; } = new();
        public DateTime? UpdatedAt { get; set; }
        /// <summary>For a college without its own account: whether the platform default will be used.</summary>
        public bool DefaultExists { get; set; }
        /// <summary>The gateways this server can use (the test gateway only outside Production).</summary>
        public List<PaymentProviderDto> Providers { get; set; } = new();
    }

    public class PaymentAccountInput
    {
        public string? Provider { get; set; }
        public string? MerchantId { get; set; }
        public bool IsTestMode { get; set; } = true;
        public bool IsEnabled { get; set; } = true;
        /// <summary>Blank or missing keeps the saved value.</summary>
        public Dictionary<string, string?>? Secrets { get; set; }
    }

    public interface IPaymentAccountService
    {
        /// <summary>collegeId null = the platform default.</summary>
        Task<PaymentAccountView> GetAsync(Guid? collegeId);
        Task<PaymentAccountView> SaveAsync(Guid? collegeId, PaymentAccountInput input);
        Task DeleteAsync(Guid? collegeId);
    }

    public interface IPaymentAccountResolver
    {
        /// <summary>The college's own enabled account, else the enabled platform default, else null.</summary>
        Task<(PaymentAccount? Account, bool IsDefault)> ForCollegeAsync(Guid collegeId);

        Task<PaymentAccount?> ByIdAsync(Guid accountId);

        /// <summary>The account with its keys decrypted, for one gateway call.</summary>
        GatewayAccount Open(PaymentAccount account);

        /// <summary>The adapter for a provider, or null when this server can't use it (unknown, or test gateway in Production).</summary>
        IPaymentGateway? Gateway(string provider);
    }

    public sealed class PaymentAccountResolver : IPaymentAccountResolver
    {
        private readonly ApplicationDbContext _context;
        private readonly ISecretProtector _protector;
        private readonly IEnumerable<IPaymentGateway> _gateways;
        private readonly bool _production;

        public PaymentAccountResolver(ApplicationDbContext context, ISecretProtector protector, IEnumerable<IPaymentGateway> gateways, IHostEnvironment env)
        {
            _context = context;
            _protector = protector;
            _gateways = gateways;
            _production = env.IsProduction();
        }

        public async Task<(PaymentAccount? Account, bool IsDefault)> ForCollegeAsync(Guid collegeId)
        {
            var accounts = await _context.PaymentAccounts.AsNoTracking()
                .Where(a => (a.CollegeId == collegeId || a.CollegeId == null) && a.IsEnabled)
                .ToListAsync();
            var own = accounts.FirstOrDefault(a => a.CollegeId == collegeId);
            if (own != null) return (own, false);
            var fallback = accounts.FirstOrDefault(a => a.CollegeId == null);
            return (fallback, fallback != null);
        }

        public Task<PaymentAccount?> ByIdAsync(Guid accountId) =>
            _context.PaymentAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == accountId);

        public GatewayAccount Open(PaymentAccount account)
        {
            var secrets = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(account.SecretsProtected))
                secrets = JsonSerializer.Deserialize<Dictionary<string, string>>(_protector.Unprotect(account.SecretsProtected)) ?? new();
            return new GatewayAccount(account.Id, account.Provider, account.MerchantId, secrets, account.IsTestMode);
        }

        public IPaymentGateway? Gateway(string provider)
        {
            var gateway = _gateways.FirstOrDefault(g => string.Equals(g.Provider, provider, StringComparison.OrdinalIgnoreCase));
            if (gateway == null || (_production && !gateway.AllowedInProduction)) return null;
            return gateway;
        }

        public IEnumerable<IPaymentGateway> Usable() => _gateways.Where(g => !_production || g.AllowedInProduction);
    }

    /// <summary>
    /// Platform admin only (the controller's policy): the gateway accounts students pay into. A college's own, else the
    /// platform default. Keys are stored encrypted and never returned; every change is audited (without the keys).
    /// </summary>
    public sealed class PaymentAccountService : IPaymentAccountService
    {
        private readonly ApplicationDbContext _context;
        private readonly ISecretProtector _protector;
        private readonly PaymentAccountResolver _resolver;

        public PaymentAccountService(ApplicationDbContext context, ISecretProtector protector, PaymentAccountResolver resolver)
        {
            _context = context;
            _protector = protector;
            _resolver = resolver;
        }

        private Task<PaymentAccount?> FindAsync(Guid? collegeId) =>
            _context.PaymentAccounts.FirstOrDefaultAsync(a => collegeId == null ? a.CollegeId == null : a.CollegeId == collegeId);

        private async Task<string> CollegeNameAsync(Guid? collegeId)
        {
            if (collegeId == null) return "the platform default";
            var name = await _context.Colleges.IgnoreQueryFilters().Where(c => c.CollegeId == collegeId).Select(c => c.Name).FirstOrDefaultAsync();
            return name ?? throw new KeyNotFoundException("College not found.");
        }

        private List<PaymentProviderDto> Providers() => _resolver.Usable().Select(g => new PaymentProviderDto
        {
            Provider = g.Provider,
            DisplayName = g.DisplayName,
            NeedsMerchantId = g.NeedsMerchantId,
            SecretFields = g.SecretFields.ToList(),
        }).ToList();

        public async Task<PaymentAccountView> GetAsync(Guid? collegeId)
        {
            await CollegeNameAsync(collegeId);
            var account = await FindAsync(collegeId);
            var defaultExists = collegeId == null ? account != null : await _context.PaymentAccounts.AnyAsync(a => a.CollegeId == null);
            var view = new PaymentAccountView { DefaultExists = defaultExists, Providers = Providers() };
            if (account == null) return view;

            view.Exists = true;
            view.Provider = account.Provider;
            view.MerchantId = account.MerchantId;
            view.IsTestMode = account.IsTestMode;
            view.IsEnabled = account.IsEnabled;
            view.UpdatedAt = account.UpdatedAt;
            try
            {
                view.SavedSecrets = _resolver.Open(account).Secrets.Where(s => s.Value != "").Select(s => s.Key).ToList();
            }
            catch (System.Security.Cryptography.CryptographicException)
            {
                view.SavedSecrets = new(); // the server key changed: the keys must be typed in again
            }
            return view;
        }

        public async Task<PaymentAccountView> SaveAsync(Guid? collegeId, PaymentAccountInput input)
        {
            var name = await CollegeNameAsync(collegeId);
            var provider = (input.Provider ?? "").Trim().ToUpperInvariant();
            var gateway = _resolver.Gateway(provider)
                ?? throw new InvalidOperationException(provider == PaymentProviders.Test
                    ? "The test gateway can't be used on the live server."
                    : "Choose a payment gateway.");
            var merchantId = (input.MerchantId ?? "").Trim();
            if (gateway.NeedsMerchantId && merchantId == "")
                throw new InvalidOperationException("Enter the merchant id the gateway gave you.");
            if (merchantId.Length > 100) throw new InvalidOperationException("The merchant id can be at most 100 characters.");

            var account = await FindAsync(collegeId);
            var isNew = account == null;
            var providerChanged = account != null && account.Provider != gateway.Provider;

            // Keys: start from the saved ones (unless the gateway changed), then apply what was typed.
            var secrets = new Dictionary<string, string>();
            if (account != null && !providerChanged && !string.IsNullOrEmpty(account.SecretsProtected))
            {
                try { secrets = new Dictionary<string, string>(_resolver.Open(account).Secrets); }
                catch (System.Security.Cryptography.CryptographicException) { secrets = new(); }
            }
            var changedKeys = new List<string>();
            foreach (var field in gateway.SecretFields)
            {
                var typed = input.Secrets != null && input.Secrets.TryGetValue(field.Key, out var v) ? (v ?? "").Trim() : "";
                if (typed.Length > 1000) throw new InvalidOperationException($"{field.Label} is too long.");
                if (typed != "")
                {
                    secrets[field.Key] = typed;
                    changedKeys.Add(field.Label);
                }
                if (!secrets.TryGetValue(field.Key, out var have) || have == "")
                    throw new InvalidOperationException($"Enter the {field.Label}.");
            }
            // Only the gateway's own fields are kept.
            secrets = secrets.Where(s => gateway.SecretFields.Any(f => f.Key == s.Key)).ToDictionary(s => s.Key, s => s.Value);

            if (account == null)
            {
                account = new PaymentAccount { Id = Guid.NewGuid(), CollegeId = collegeId, Provider = gateway.Provider };
                _context.PaymentAccounts.Add(account);
            }
            account.Provider = gateway.Provider;
            account.MerchantId = merchantId == "" ? null : merchantId;
            account.IsTestMode = input.IsTestMode || !gateway.AllowedInProduction;
            account.IsEnabled = input.IsEnabled;
            account.SecretsProtected = secrets.Count == 0 ? null : _protector.Protect(JsonSerializer.Serialize(secrets));
            account.UpdatedAt = DateTime.UtcNow;
            account.UpdatedBy = _context.CurrentUserId;

            Audit(collegeId, account.Id, isNew ? "PaymentAccountAdded" : "PaymentAccountChanged",
                $"Gateway for {name}: {gateway.DisplayName}{(account.MerchantId != null ? $", merchant {account.MerchantId}" : "")}, "
                + $"{(account.IsTestMode ? "test mode" : "LIVE")}, {(account.IsEnabled ? "on" : "off")}; "
                + (changedKeys.Count > 0 ? $"keys changed: {string.Join(", ", changedKeys)}." : "keys kept."));
            await _context.SaveChangesAsync();
            return await GetAsync(collegeId);
        }

        public async Task DeleteAsync(Guid? collegeId)
        {
            var name = await CollegeNameAsync(collegeId);
            var account = await FindAsync(collegeId);
            if (account == null) return;
            if (await _context.PaymentOrders.IgnoreQueryFilters().AnyAsync(o => o.PaymentAccountId == account.Id))
                throw new InvalidOperationException("Payments were made through this account, so it can't be removed (its records point to it). Switch it off instead.");
            _context.PaymentAccounts.Remove(account);
            Audit(collegeId, account.Id, "PaymentAccountRemoved", $"Gateway for {name} removed ({account.Provider}).");
            await _context.SaveChangesAsync();
        }

        private void Audit(Guid? collegeId, Guid accountId, string evt, string details) =>
            _context.AuditLogs.Add(new AuditLog
            {
                Id = Guid.NewGuid(),
                CollegeId = collegeId,
                Action = "UPDATE",
                TableName = nameof(PaymentAccount),
                RecordId = accountId.ToString(),
                PerformedBy = _context.CurrentUserId,
                UserType = "Staff",
                Event = evt,
                Details = details.Length > 1000 ? details[..1000] : details,
            });
    }
}
