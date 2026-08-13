using System;
using System.Text.RegularExpressions;

namespace RotinaRemote.Core.Models
{
    public readonly struct DeviceId : IEquatable<DeviceId>
    {
        public string RawValue { get; }

        public DeviceId(string rawValue)
        {
            var cleaned = Regex.Replace(rawValue ?? "", @"\D", "");
            if (cleaned.Length != 9)
            {
                throw new ArgumentException("ID do dispositivo deve ter exatamente 9 dígitos numéricos.", nameof(rawValue));
            }
            RawValue = cleaned;
        }

        public string Formatted => $"{RawValue.Substring(0, 3)} {RawValue.Substring(3, 3)} {RawValue.Substring(6, 3)}";

        public static bool TryParse(string input, out DeviceId deviceId)
        {
            try
            {
                var cleaned = Regex.Replace(input ?? "", @"\D", "");
                if (cleaned.Length == 9)
                {
                    deviceId = new DeviceId(cleaned);
                    return true;
                }
            }
            catch
            {
                // Ignore parse errors
            }

            deviceId = default;
            return false;
        }

        public override string ToString() => Formatted;

        public bool Equals(DeviceId other) => RawValue == other.RawValue;
        public override bool Equals(object? obj) => obj is DeviceId other && Equals(other);
        public override int GetHashCode() => RawValue?.GetHashCode() ?? 0;
        public static bool operator ==(DeviceId left, DeviceId right) => left.Equals(right);
        public static bool operator !=(DeviceId left, DeviceId right) => !left.Equals(right);
    }
}
