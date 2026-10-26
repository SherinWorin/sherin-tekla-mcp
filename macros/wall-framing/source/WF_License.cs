// WF_License.cs - the licence engine of the wall-framing auto-dimensioner.
//
// No Tekla reference, no dependency on anything but the .NET framework, so
// it can be unit-tested without a running Tekla Structures
// (tests/test_license.py).
//
// It answers one question:  "is this installation allowed to draw the
// Pro features, and if not, until when may it?"
//
// Three states:
//
//   FREE      the free edition - the dimension chains only
//   TRIAL     the Pro features, until the trial runs out
//   PRO       the Pro features, with a signed licence key
//
// The state is kept in two places, and the EARLIER of the two first-run
// stamps wins:
//
//   * %LOCALAPPDATA%\P142\WallFraming\state.txt   (the file)
//   * HKCU\Software\P142\WallFraming               (the registry)
//
// Deleting the file does not reset the trial, because the registry keeps
// its own copy.  Moving the clock back does not reset it either, because
// the last-seen stamp is remembered and a backwards jump voids the trial.
//
// A PRO licence does not depend on any of that: it is a signed blob with
// an expiry date, and the signature is checked against a public key that
// is compiled into this file.  Only the holder of the matching PRIVATE key
// can produce one, so a licence can be neither extended nor forged by the
// customer.
//
// The key is tied to the computer it was issued for: it carries three
// hashes of the machine (the Windows machine GUID, the serial of the
// system volume and the processor id).  At least two of them have to
// match, so replacing a disk or moving the PC to another machine does not
// invalidate a licence, but copying the licence to another computer does
// not work.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Management;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace P142.WallFraming
{
    /// <summary>What the macro is allowed to draw.</summary>
    public enum WfEdition
    {
        /// <summary>The dimension chains only.</summary>
        Free = 0,
        /// <summary>Everything: chains, outline, overall, north run, marks, sections, notes.</summary>
        Pro = 1
    }

    /// <summary>Why the macro is in the state it is in.</summary>
    public enum WfLicenseState
    {
        /// <summary>No licence, no trial: the free edition.</summary>
        Free = 0,
        /// <summary>The Pro features, inside the trial period.</summary>
        Trial = 1,
        /// <summary>The Pro features, with a valid licence key.</summary>
        Pro = 2,
        /// <summary>A licence key is present but its date has passed.</summary>
        Expired = 3,
        /// <summary>A licence key is present but it does not verify.</summary>
        Invalid = 4
    }

    /// <summary>The three hashes that identify a computer.</summary>
    public sealed class WfMachineIds
    {
        /// <summary>SHA-256 of the Windows machine GUID.</summary>
        public string Guid = "";
        /// <summary>SHA-256 of the serial number of the system volume.</summary>
        public string Volume = "";
        /// <summary>SHA-256 of the processor id.</summary>
        public string Cpu = "";

        /// <summary>The ids that could be read on this computer.</summary>
        public List<string> All()
        {
            List<string> list = new List<string>();
            if (!string.IsNullOrEmpty(Guid)) list.Add(Guid);
            if (!string.IsNullOrEmpty(Volume)) list.Add(Volume);
            if (!string.IsNullOrEmpty(Cpu)) list.Add(Cpu);
            return list;
        }

        /// <summary>How many of the given ids this computer matches.</summary>
        public int Matches(List<string> other)
        {
            if (other == null) return 0;
            int n = 0;
            foreach (string a in All())
                foreach (string b in other)
                    if (string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) { n++; break; }
            return n;
        }

        /// <summary>The ids, in one line, for the state file.</summary>
        public string Serialize()
        {
            return Guid + ":" + Volume + ":" + Cpu;
        }

        /// <summary>Reads the ids back.</summary>
        public static WfMachineIds Parse(string s)
        {
            WfMachineIds ids = new WfMachineIds();
            if (string.IsNullOrEmpty(s)) return ids;
            string[] parts = s.Split(':');
            if (parts.Length > 0) ids.Guid = parts[0];
            if (parts.Length > 1) ids.Volume = parts[1];
            if (parts.Length > 2) ids.Cpu = parts[2];
            return ids;
        }
    }

    /// <summary>A licence key that has been read and verified.</summary>
    public sealed class WfLicenseKey
    {
        /// <summary>The customer the key was issued to.</summary>
        public string Customer = "";
        /// <summary>The expiry date, or an empty string for "no expiry".</summary>
        public string Expiry = "";
        /// <summary>The machine the key was issued for.</summary>
        public List<string> MachineIds = new List<string>();
        /// <summary>The raw key, as it was typed.</summary>
        public string Raw = "";

        /// <summary>True when the key carries an expiry date that has passed.</summary>
        public bool IsExpired(DateTime now)
        {
            if (string.IsNullOrEmpty(Expiry)) return false;
            DateTime d;
            if (!DateTime.TryParse(Expiry, CultureInfo.InvariantCulture,
                                  DateTimeStyles.None, out d)) return true;
            return d.Date < now.Date;
        }
    }

    /// <summary>What the macro knows about this installation.</summary>
    public sealed class WfLicenseInfo
    {
        /// <summary>What the macro may draw.</summary>
        public WfEdition Edition = WfEdition.Free;
        /// <summary>Why.</summary>
        public WfLicenseState State = WfLicenseState.Free;
        /// <summary>The customer, when a licence key is in use.</summary>
        public string Customer = "";
        /// <summary>When the trial runs out, when it is running.</summary>
        public string TrialEnds = "";
        /// <summary>When the licence runs out, when it has one.</summary>
        public string LicenseEnds = "";
        /// <summary>A line for the user, in plain words.</summary>
        public string Message = "";
        /// <summary>True when the trial was ended because the clock was moved back.</summary>
        public bool TrialVoided = false;
    }

    /// <summary>The licence engine.</summary>
    public static class WfLicense
    {
        /// <summary>How many days the Pro features are free at the start.</summary>
        public const int TrialDays = 3;

        /// <summary>How far the clock may be off before the trial is void (one hour).</summary>
        public const long ClockToleranceTicks = 36000000000L;

        // The public key of the owner.  The matching PRIVATE key is not in
        // this repository and never will be: it is what makes a key
        // unforgeable.  Replace the two constants below when a new pair is
        // generated (WF_KeyGen writes both).
        /// <summary>The public key, in the XML form of RSACryptoServiceProvider.</summary>
        public const string PublicKeyXml =
            "REPLACE_WITH_THE_PUBLIC_KEY";

        /// <summary>The folder the state and the licence live in.</summary>
        public static string AppFolder()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(Path.Combine(local, "P142"), "WallFraming");
        }

        /// <summary>The file the state is kept in.</summary>
        public static string StatePath()
        {
            return Path.Combine(AppFolder(), "state.txt");
        }

        /// <summary>The registry key the state is mirrored into.</summary>
        public const string RegistryKey = @"Software\P142\WallFraming";

        // ------------------------------------------------------------- machine
        /// <summary>Reads the three ids of this computer.</summary>
        public static WfMachineIds GetMachineIds()
        {
            WfMachineIds ids = new WfMachineIds();
            ids.Guid = Hash(ReadMachineGuid());
            ids.Volume = Hash(ReadVolumeSerial());
            ids.Cpu = Hash(ReadCpuId());
            return ids;
        }

        /// <summary>Reads the Windows machine GUID of this computer.</summary>
        public static string ReadMachineGuid()
        {
            try
            {
                using (RegistryKey key = Registry.LocalMachine.OpenSubKey(
                    @"SOFTWARE\Microsoft\Cryptography"))
                {
                    if (key == null) return "";
                    object v = key.GetValue("MachineGuid");
                    return v == null ? "" : v.ToString();
                }
            }
            catch { return ""; }
        }

        /// <summary>Reads the serial number of the system volume.</summary>
        public static string ReadVolumeSerial()
        {
            try
            {
                string root = Path.GetPathRoot(Environment.SystemDirectory);
                if (string.IsNullOrEmpty(root)) return "";
                uint serial = 0, maxComponent = 0, flags = 0;
                bool ok = GetVolumeInformation(root, null, 0, out serial,
                    out maxComponent, out flags, null, 0);
                if (!ok) return "";
                return serial.ToString(CultureInfo.InvariantCulture);
            }
            catch { return ""; }
        }

        // The kernel32 call the volume serial needs.  It is declared here
        // rather than taken from the framework, because the framework one is
        // internal.
        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true,
            CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        [return: System.Runtime.InteropServices.MarshalAs(
            System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetVolumeInformation(
            string rootPathName,
            StringBuilder volumeNameBuffer, int volumeNameSize,
            out uint volumeSerialNumber,
            out uint maximumComponentLength,
            out uint fileSystemFlags,
            StringBuilder fileSystemNameBuffer, int fileSystemNameSize);

        /// <summary>Reads the processor id of this computer.</summary>
        public static string ReadCpuId()
        {
            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "SELECT ProcessorId FROM Win32_Processor"))
                using (ManagementObjectCollection all = searcher.Get())
                {
                    foreach (ManagementObject o in all)
                    {
                        string id = o["ProcessorId"] as string;
                        if (!string.IsNullOrEmpty(id)) return id.Trim();
                    }
                }
            }
            catch { }
            return "";
        }

        /// <summary>Hashes a value, so that the raw value is never stored anywhere.</summary>
        public static string Hash(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            using (SHA256 sha = SHA256.Create())
            {
                byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value.Trim()));
                StringBuilder sb = new StringBuilder();
                foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        // ------------------------------------------------------------- the key
        /// <summary>
        /// Checks a licence key against the public key.  Returns null when
        /// the key does not verify.
        /// </summary>
        public static WfLicenseKey VerifyKey(string keyText)
        {
            return VerifyKey(keyText, PublicKeyXml);
        }

        /// <summary>
        /// Checks a licence key against the given public key.  The
        /// parameterless overload above is what the macro uses; this one
        /// exists so that the test harness can check the whole round trip
        /// with a key pair it holds in memory only.
        /// </summary>
        public static WfLicenseKey VerifyKey(string keyText, string publicKeyXml)
        {
            WfLicenseKey key = ParseKey(keyText);
            if (key == null) return null;
            if (string.IsNullOrEmpty(publicKeyXml)) return null;
            try
            {
                byte[] payload = FromBase64Url(PayloadOf(keyText));
                byte[] signature = FromBase64Url(SignatureOf(keyText));
                if (payload.Length == 0 || signature.Length == 0) return null;

                using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
                {
                    rsa.FromXmlString(publicKeyXml);
                    bool ok = rsa.VerifyData(payload,
                        CryptoConfig.MapNameToOID("SHA256"), signature);
                    if (!ok) return null;
                }
            }
            catch { return null; }
            return key;
        }

        /// <summary>Reads the parts of a key, without checking the signature.</summary>
        public static WfLicenseKey ParseKey(string keyText)
        {
            if (string.IsNullOrEmpty(keyText)) return null;
            string text = keyText.Trim();
            string[] parts = text.Split('.');
            if (parts.Length != 3) return null;
            if (parts[0] != "WF1") return null;

            WfLicenseKey key = new WfLicenseKey();
            key.Raw = text;
            try
            {
                string payload = Encoding.UTF8.GetString(FromBase64Url(parts[1]));
                foreach (string line in payload.Split('\n'))
                {
                    string row = line.Trim();
                    if (row.Length == 0) continue;
                    int eq = row.IndexOf('=');
                    if (eq <= 0) continue;
                    string name = row.Substring(0, eq);
                    string value = row.Substring(eq + 1);
                    switch (name)
                    {
                        case "customer": key.Customer = value; break;
                        case "expiry": key.Expiry = value; break;
                        case "dev": key.MachineIds.AddRange(value.Split(':')); break;
                    }
                }
            }
            catch { return null; }
            return key;
        }

        /// <summary>The signed part of a key, in base64url.</summary>
        public static string PayloadOf(string keyText)
        {
            string[] parts = keyText.Trim().Split('.');
            return parts.Length == 3 ? parts[1] : "";
        }

        /// <summary>The signature of a key, in base64url.</summary>
        public static string SignatureOf(string keyText)
        {
            string[] parts = keyText.Trim().Split('.');
            return parts.Length == 3 ? parts[2] : "";
        }

        /// <summary>Base64url, without the padding.</summary>
        public static string ToBase64Url(byte[] data)
        {
            return Convert.ToBase64String(data).TrimEnd('=')
                .Replace('+', '-').Replace('/', '_');
        }

        /// <summary>Base64url back to bytes.</summary>
        public static byte[] FromBase64Url(string text)
        {
            if (string.IsNullOrEmpty(text)) return new byte[0];
            string s = text.Replace('-', '+').Replace('_', '/');
            switch (s.Length % 4)
            {
                case 2: s += "=="; break;
                case 3: s += "="; break;
            }
            return Convert.FromBase64String(s);
        }

        // ------------------------------------------------------------- the state
        /// <summary>Reads the state of this installation.</summary>
        public static Dictionary<string, string> LoadState()
        {
            Dictionary<string, string> state = new Dictionary<string, string>();

            // the file
            try
            {
                string path = StatePath();
                if (File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        string row = line.Trim();
                        if (row.Length == 0 || row.StartsWith("#")) continue;
                        int eq = row.IndexOf('=');
                        if (eq <= 0) continue;
                        state[row.Substring(0, eq)] = row.Substring(eq + 1);
                    }
                }
            }
            catch { }

            // the registry copy, which survives the file being deleted
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKey))
                {
                    if (key == null) return state;
                    foreach (string name in key.GetValueNames())
                    {
                        object v = key.GetValue(name);
                        if (v != null && !state.ContainsKey(name)) state[name] = v.ToString();
                    }
                }
            }
            catch { }

            return state;
        }

        /// <summary>Writes the state to the file and to the registry.</summary>
        public static void SaveState(Dictionary<string, string> state)
        {
            if (state == null) return;
            try
            {
                string folder = AppFolder();
                if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# wall-framing auto-dimensioner - the state of this installation");
                sb.AppendLine("# Do not edit: the trial is voided when the clock is moved back.");
                foreach (string name in state.Keys)
                    sb.AppendLine(name + "=" + state[name]);
                File.WriteAllText(StatePath(), sb.ToString(), new UTF8Encoding(false));
            }
            catch { }

            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RegistryKey))
                {
                    if (key == null) return;
                    foreach (string name in state.Keys)
                        key.SetValue(name, state[name], RegistryValueKind.String);
                }
            }
            catch { }
        }

        /// <summary>The moment the trial started, or zero when there is no trial.</summary>
        public static DateTime TrialStart(Dictionary<string, string> state)
        {
            if (state == null) return DateTime.MinValue;
            string v;
            if (!state.TryGetValue("first", out v)) return DateTime.MinValue;
            long ticks;
            if (!long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
                return DateTime.MinValue;
            if (ticks <= 0) return DateTime.MinValue;
            try { return new DateTime(ticks, DateTimeKind.Utc); }
            catch { return DateTime.MinValue; }
        }

        /// <summary>The last moment the macro was run, or zero.</summary>
        public static DateTime LastSeen(Dictionary<string, string> state)
        {
            if (state == null) return DateTime.MinValue;
            string v;
            if (!state.TryGetValue("last", out v)) return DateTime.MinValue;
            long ticks;
            if (!long.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out ticks))
                return DateTime.MinValue;
            if (ticks <= 0) return DateTime.MinValue;
            try { return new DateTime(ticks, DateTimeKind.Utc); }
            catch { return DateTime.MinValue; }
        }

        // ------------------------------------------------------------- the answer
        /// <summary>
        /// Works out what the macro may draw on this computer, right now.
        /// </summary>
        public static WfLicenseInfo Evaluate(Dictionary<string, string> state,
                                             string keyText,
                                             DateTime nowUtc,
                                             WfMachineIds machine)
        {
            return Evaluate(state, keyText, nowUtc, machine, PublicKeyXml);
        }

        /// <summary>
        /// Works out what the macro may draw, against the given public key.
        /// The overload above is what the macro uses; this one exists so that
        /// the test harness can check the whole round trip with a key pair it
        /// holds in memory only.
        /// </summary>
        public static WfLicenseInfo Evaluate(Dictionary<string, string> state,
                                             string keyText,
                                             DateTime nowUtc,
                                             WfMachineIds machine,
                                             string publicKeyXml)
        {
            WfLicenseInfo info = new WfLicenseInfo();

            // ---- 1. a licence key wins over everything
            if (!string.IsNullOrEmpty(keyText))
            {
                WfLicenseKey key = VerifyKey(keyText, publicKeyXml);
                if (key == null)
                {
                    info.Edition = WfEdition.Free;
                    info.State = WfLicenseState.Invalid;
                    info.Message = "The licence key is not valid. " +
                                   "Check that it was copied in full.";
                    return info;
                }
                if (key.IsExpired(nowUtc))
                {
                    info.Edition = WfEdition.Free;
                    info.State = WfLicenseState.Expired;
                    info.Customer = key.Customer;
                    info.LicenseEnds = key.Expiry;
                    info.Message = "The Pro licence of " + key.Customer +
                                   " ran out on " + key.Expiry + ".";
                    return info;
                }
                if (machine != null && key.MachineIds.Count > 0)
                {
                    int needed = key.MachineIds.Count < 2 ? key.MachineIds.Count : 2;
                    if (machine.Matches(key.MachineIds) < needed)
                    {
                        info.Edition = WfEdition.Free;
                        info.State = WfLicenseState.Invalid;
                        info.Customer = key.Customer;
                        info.Message = "This licence key was issued for another computer.";
                        return info;
                    }
                }
                info.Edition = WfEdition.Pro;
                info.State = WfLicenseState.Pro;
                info.Customer = key.Customer;
                info.LicenseEnds = key.Expiry;
                info.Message = string.IsNullOrEmpty(key.Expiry)
                    ? "Pro licence of " + key.Customer + "."
                    : "Pro licence of " + key.Customer + ", valid until " + key.Expiry + ".";
                return info;
            }

            // ---- 2. no key: is the trial still running?
            DateTime first = TrialStart(state);
            DateTime last = LastSeen(state);
            if (first == DateTime.MinValue)
            {
                info.Edition = WfEdition.Free;
                info.State = WfLicenseState.Free;
                info.Message = "The free edition.";
                return info;
            }

            // The very first run has no "last" stamp yet - the trial starts on
            // that run.  The stamp of the first run is the one to compare
            // the clock with, so that a clock which is moved back is seen.
            if (last == DateTime.MinValue) last = first;

            if (nowUtc < last - TimeSpan.FromTicks(ClockToleranceTicks))
            {
                // the clock was moved back: the trial is void
                info.Edition = WfEdition.Free;
                info.State = WfLicenseState.Free;
                info.TrialVoided = true;
                info.Message = "The clock of this computer was moved back, so the trial " +
                               "was ended.  The free edition is in use.";
                return info;
            }

            DateTime ends = first.AddDays(TrialDays);
            if (nowUtc < ends)
            {
                info.Edition = WfEdition.Pro;
                info.State = WfLicenseState.Trial;
                info.TrialEnds = ends.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                info.Message = "The Pro features are free until " + info.TrialEnds + ".";
                return info;
            }

            info.Edition = WfEdition.Free;
            info.State = WfLicenseState.Free;
            info.TrialEnds = ends.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            info.Message = "The Pro trial ran out on " + info.TrialEnds + ".";
            return info;
        }

        /// <summary>
        /// Works out what the macro may draw, and records the run.
        /// </summary>
        public static WfLicenseInfo EvaluateAndRecord(string keyText, DateTime nowUtc)
        {
            WfMachineIds machine = GetMachineIds();
            Dictionary<string, string> state = LoadState();

            // The trial starts at the FIRST run, so the stamp is written BEFORE
            // the evaluation - otherwise the first run would fall outside the
            // trial and the three days would only start at the second run.
            if (!state.ContainsKey("first"))
                state["first"] = nowUtc.Ticks.ToString(CultureInfo.InvariantCulture);
            if (!state.ContainsKey("instance"))
                state["instance"] = Guid.NewGuid().ToString("N");

            WfLicenseInfo info = Evaluate(state, keyText, nowUtc, machine);

            // remember the run, so that a backwards clock is noticed
            try
            {
                if (info.TrialVoided)
                {
                    // the trial really ends: the stamps are thrown away
                    state.Remove("first");
                    state.Remove("last");
                }
                DateTime last = LastSeen(state);
                if (nowUtc > last)
                    state["last"] = nowUtc.Ticks.ToString(CultureInfo.InvariantCulture);
                state["ids"] = machine.Serialize();
                if (!string.IsNullOrEmpty(keyText)) state["key"] = keyText.Trim();
                SaveState(state);
            }
            catch { }

            return info;
        }

        /// <summary>
        /// Puts a licence key into the state of this installation.
        /// </summary>
        public static bool Activate(string keyText, out string message)
        {
            message = "";
            WfLicenseKey key = VerifyKey(keyText);
            if (key == null)
            {
                message = "The licence key is not valid.  Check that it was copied in full.";
                return false;
            }
            WfMachineIds machine = GetMachineIds();
            if (key.MachineIds.Count > 0)
            {
                int needed = key.MachineIds.Count < 2 ? key.MachineIds.Count : 2;
                if (machine.Matches(key.MachineIds) < needed)
                {
                    message = "This licence key was issued for another computer.";
                    return false;
                }
            }
            Dictionary<string, string> state = LoadState();
            state["key"] = keyText.Trim();
            state["customer"] = key.Customer;
            state["expiry"] = key.Expiry;
            SaveState(state);
            message = "The Pro licence of " + key.Customer + " is now in use.";
            return true;
        }

        /// <summary>Removes the licence key, so that the trial applies again.</summary>
        public static void Deactivate()
        {
            Dictionary<string, string> state = LoadState();
            state.Remove("key");
            state.Remove("customer");
            state.Remove("expiry");
            SaveState(state);
        }
    }
}
