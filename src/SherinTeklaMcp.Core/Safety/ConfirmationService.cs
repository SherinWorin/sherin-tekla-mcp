using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace SherinTeklaMcp.Core.Safety
{
    /// <summary>
    /// Two-step confirmation for model changes.
    ///
    /// A preview returns a token bound to (operation + the exact normalized arguments + expiry),
    /// signed with a random key that lives only in this process. An apply call is accepted only
    /// with a valid, unexpired token for the SAME arguments. So the AI cannot apply something it
    /// did not preview first, cannot change the arguments after the user saw the plan, and a
    /// token from an earlier server run is never valid.
    /// </summary>
    public sealed class ConfirmationService
    {
        private readonly byte[] _key;
        private readonly Func<DateTime> _utcNow;

        public ConfirmationService() : this(null, null) { }

        /// <summary>For tests: fixed key and clock.</summary>
        public ConfirmationService(byte[]? key, Func<DateTime>? utcNow)
        {
            if (key == null)
            {
                key = new byte[32];
                using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(key);
            }
            _key = key;
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public string CreateToken(string operation, string canonicalArguments, TimeSpan validFor, out DateTime expiresUtc)
        {
            expiresUtc = _utcNow().Add(validFor);
            var ticks = expiresUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            return Sign(operation, canonicalArguments, ticks) + "." + ticks;
        }

        /// <summary>Returns null when valid, otherwise the reason it was rejected.</summary>
        public string? Verify(string? token, string operation, string canonicalArguments)
        {
            if (string.IsNullOrWhiteSpace(token))
                return "apply=true needs the confirmToken from a preview. Call this tool with apply=false first, show the plan to the user, and only apply after the user agrees.";

            var dot = token!.LastIndexOf('.');
            if (dot <= 0 || dot == token.Length - 1)
                return "The confirmToken is malformed. Run the preview again.";

            var signature = token.Substring(0, dot);
            var ticksText = token.Substring(dot + 1);
            if (!long.TryParse(ticksText, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks))
                return "The confirmToken is malformed. Run the preview again.";

            var expected = Sign(operation, canonicalArguments, ticksText);
            if (!FixedTimeEquals(signature, expected))
                return "The confirmToken does not match these arguments (they changed after the preview, or the token belongs to another operation or an earlier server run). Run the preview again and confirm with the user.";

            if (_utcNow().Ticks > ticks)
                return "The confirmToken has expired. Run the preview again and confirm with the user.";

            return null;
        }

        private string Sign(string operation, string canonicalArguments, string ticks)
        {
            using (var hmac = new HMACSHA256(_key))
            {
                var payload = Encoding.UTF8.GetBytes(operation + "\n" + canonicalArguments + "\n" + ticks);
                var hash = hmac.ComputeHash(payload);
                return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
            }
        }

        private static bool FixedTimeEquals(string a, string b)
        {
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (var i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }
    }
}
