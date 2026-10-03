using System;

namespace RotinaRemote.Core.Models
{
    public class LicenseInfo
    {
        public string LicenseKey { get; set; } = string.Empty;
        public string Plan { get; set; } = "Free"; // "Free" ou "Premium"
        public bool IsPremium => string.Equals(Plan, "Premium", StringComparison.OrdinalIgnoreCase);
        public string LicensedTo { get; set; } = "Utilizador Gratuito";
        public DateTime? ValidUntil { get; set; }
        public string StatusText => IsPremium 
            ? (ValidUntil.HasValue ? $"Premium (Válido até {ValidUntil.Value:dd/MM/yyyy})" : "Premium Vitalício (Ativo)") 
            : "Versão Gratuita (Free)";
        public string Message { get; set; } = string.Empty;
    }
}
