// WF_KeyGen.cs - the tool that issues licence keys.
//
// This is the OWNER's tool.  It signs a key with the owner's PRIVATE key,
// which is not in the repository and never will be.  Without that private
// key nobody can produce a working licence key.
//
//   WF_KeyGen newkey    <private.xml> <public.xml>      make a new key pair
//   WF_KeyGen issue     <private.xml> -c "Customer" [-e 2027-12-31]
//                                               [-d guid,volume,cpu]
//                                               [-m thispc] [-p "a note"]
//   WF_KeyGen machine                                     the ids of this PC
//   WF_KeyGen sign      <private.xml> -k "<key>" -p "a note"
//   WF_KeyGen install   "<key>"                          put a key in this PC
//   WF_KeyGen status                                      the licence of this PC
//   WF_KeyGen remove                                      take the licence away
//
// The machine ids of the computer the key is issued FOR go into the key.
// Use -m thispc on that computer, or -d with the three ids taken from
// "WF_KeyGen machine" there.
//
// A key looks like this:
//
//   WF1.<payload>.<signature>
//
// and carries the customer, the expiry date and the machine ids.  The
// signature is RSA-2048 over SHA-256 of the payload, so a key can be
// neither forged nor extended by the customer.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace P142.WallFraming
{
    /// <summary>The command line of the key generator.</summary>
    public static class WfKeyGen
    {
        /// <summary>Runs the tool.</summary>
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0) { Usage(); return 1; }

                switch (args[0].ToLowerInvariant())
                {
                    case "newkey": return NewKey(args);
                    case "issue": return Issue(args);
                    case "machine": return Machine(args);
                    case "sign": return Sign(args);
                    case "install": return Install(args);
                    case "status": return Status(args);
                    case "remove": return Remove(args);
                    default:
                        Usage();
                        return 1;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine("ERROR: " + e.Message);
                return 2;
            }
        }

        /// <summary>Prints how the tool is used.</summary>
        public static void Usage()
        {
            Console.WriteLine("WF_KeyGen - issues the licence keys of the wall-framing auto-dimensioner");
            Console.WriteLine();
            Console.WriteLine("  WF_KeyGen newkey <private.xml> <public.xml>");
            Console.WriteLine("      makes a new key pair.  KEEP THE PRIVATE FILE SAFE.");
            Console.WriteLine();
            Console.WriteLine("  WF_KeyGen machine");
            Console.WriteLine("      the three ids of THIS computer, for use with -d");
            Console.WriteLine();
            Console.WriteLine("  WF_KeyGen issue <private.xml> -c <customer> [-e <yyyy-mm-dd>]");
            Console.WriteLine("                          [-d <id,id,id>] [-m thispc] [-p <note>]");
            Console.WriteLine("      issues a key.  Without -e the key never expires.");
            Console.WriteLine();
            Console.WriteLine("  WF_KeyGen sign <private.xml> -k <key> -p <note>");
            Console.WriteLine("      re-signs a key with a different note.");
            Console.WriteLine();
            Console.WriteLine("  WF_KeyGen install <key>");
            Console.WriteLine("      puts a licence key into THIS computer, so that the");
            Console.WriteLine("      Pro edition works here.  The key is checked against");
            Console.WriteLine("      the machine ids of this computer.");
            Console.WriteLine();
            Console.WriteLine("  WF_KeyGen status");
            Console.WriteLine("      the licence of this computer: the edition, the");
            Console.WriteLine("      customer, the expiry and the trial.");
            Console.WriteLine();
            Console.WriteLine("  WF_KeyGen remove");
            Console.WriteLine("      takes the licence key away.  The trial applies again.");
        }

        /// <summary>Reads an option and its value out of the arguments.</summary>
        private static string Option(string[] args, string name, string fallback)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                    return args[i + 1];
            }
            return fallback;
        }

        /// <summary>True when the option is there.</summary>
        private static bool HasOption(string[] args, string name)
        {
            foreach (string a in args)
                if (string.Equals(a, name, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        /// <summary>Makes a new key pair and writes both halves.</summary>
        private static int NewKey(string[] args)
        {
            if (args.Length < 3) { Usage(); return 1; }
            string privatePath = args[1];
            string publicPath = args[2];

            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider(2048))
            {
                File.WriteAllText(privatePath, rsa.ToXmlString(true));
                File.WriteAllText(publicPath, rsa.ToXmlString(false));
            }
            Console.WriteLine("The private key was written to " + privatePath);
            Console.WriteLine("The public key  was written to " + publicPath);
            Console.WriteLine();
            Console.WriteLine("Put the public key into WF_License.cs (PublicKeyXml) and");
            Console.WriteLine("keep the private key where nobody else can reach it.");
            return 0;
        }

        /// <summary>Prints the ids of this computer.</summary>
        private static int Machine(string[] args)
        {
            WfMachineIds ids = WfLicense.GetMachineIds();
            Console.WriteLine("guid   = " + ids.Guid);
            Console.WriteLine("volume = " + ids.Volume);
            Console.WriteLine("cpu    = " + ids.Cpu);
            Console.WriteLine();
            Console.WriteLine("for -d : " + ids.Guid + "," + ids.Volume + "," + ids.Cpu);
            return 0;
        }

        /// <summary>Issues a licence key.</summary>
        private static int Issue(string[] args)
        {
            if (args.Length < 2) { Usage(); return 1; }
            string privatePath = args[1];
            string customer = Option(args, "-c", "");
            if (string.IsNullOrEmpty(customer))
            {
                Console.Error.WriteLine("ERROR: the customer is missing (-c).");
                return 1;
            }
            string expiry = Option(args, "-e", "");
            string note = Option(args, "-p", "");
            string devIds = Option(args, "-d", "");
            if (HasOption(args, "-m"))
            {
                WfMachineIds here = WfLicense.GetMachineIds();
                devIds = here.Guid + "," + here.Volume + "," + here.Cpu;
            }

            List<string> devices = new List<string>();
            foreach (string d in devIds.Split(','))
                if (!string.IsNullOrEmpty(d.Trim())) devices.Add(d.Trim());

            string key = Build(privatePath, customer, expiry, devices, note);
            if (string.IsNullOrEmpty(key)) return 2;

            Console.WriteLine(key);
            Console.WriteLine();
            Console.WriteLine("customer : " + customer);
            Console.WriteLine("expiry   : " + (expiry == "" ? "(never)" : expiry));
            Console.WriteLine("tied to  : " + (devices.Count == 0
                ? "(this computer, checked at run time)"
                : string.Join(", ", devices.ToArray())));
            Console.WriteLine("note     : " + note);
            return 0;
        }

        /// <summary>Signs an existing key again, with a different note.</summary>
        private static int Sign(string[] args)
        {
            if (args.Length < 2) { Usage(); return 1; }
            string privatePath = args[1];
            string raw = Option(args, "-k", "");
            string note = Option(args, "-p", "");
            if (string.IsNullOrEmpty(raw)) { Console.Error.WriteLine("ERROR: -k is missing."); return 1; }

            WfLicenseKey key = WfLicense.ParseKey(raw);
            if (key == null) { Console.Error.WriteLine("ERROR: the key cannot be read."); return 1; }

            string signed = Build(privatePath, key.Customer, key.Expiry, key.MachineIds, note);
            if (string.IsNullOrEmpty(signed)) return 2;
            Console.WriteLine(signed);
            return 0;
        }

        /// <summary>
        /// Puts a licence key into this computer.
        ///
        /// The key is written to the file license.txt in the app folder, and
        /// it is taken into use at once, so that the owner can see the Pro
        /// edition work before the key is handed over.
        /// </summary>
        private static int Install(string[] args)
        {
            if (args.Length < 2) { Usage(); return 1; }
            string raw = args[1].Trim();
            if (raw.StartsWith("\"") && raw.EndsWith("\"") && raw.Length > 1)
                raw = raw.Substring(1, raw.Length - 2).Trim();

            string folder = WfLicense.AppFolder();
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "license.txt");
            File.WriteAllText(path, raw);
            Console.WriteLine("The key was written to " + path);

            string message;
            if (WfLicense.Activate(raw, out message))
            {
                Console.WriteLine(message);
                Console.WriteLine("The Pro edition is now in use on this computer.");
                return 0;
            }
            Console.Error.WriteLine("WARNING: " + message);
            Console.Error.WriteLine("          The key is in the file, but it does not work.");
            Console.Error.WriteLine("          The public key of WF_License.cs is still the");
            Console.Error.WriteLine("          placeholder - the owner has to put the real");
            Console.Error.WriteLine("          public key in it and rebuild.");
            return 1;
        }

        /// <summary>Prints the licence of this computer.</summary>
        private static int Status(string[] args)
        {
            Dictionary<string, string> state = WfLicense.LoadState();
            string key = "";
            state.TryGetValue("key", out key);
            WfLicenseInfo info = WfLicense.EvaluateAndRecord(key, DateTime.UtcNow);

            Console.WriteLine("edition  : " + info.Edition);
            Console.WriteLine("state    : " + info.State);
            Console.WriteLine("customer : " + (info.Customer == "" ? "(none)" : info.Customer));
            Console.WriteLine("licence  : " + (info.LicenseEnds == "" ? "(never ends)"
                                                             : "ends " + info.LicenseEnds));
            Console.WriteLine("trial    : " + (info.TrialEnds == "" ? "(no trial)"
                                                             : "ends " + info.TrialEnds));
            Console.WriteLine();
            Console.WriteLine(info.Message);
            return 0;
        }

        /// <summary>Takes the licence key away from this computer.</summary>
        private static int Remove(string[] args)
        {
            WfLicense.Deactivate();
            string path = Path.Combine(WfLicense.AppFolder(), "license.txt");
            try { if (File.Exists(path)) File.Delete(path); } catch { }
            Console.WriteLine("The licence key was taken away.  The trial applies again.");
            return 0;
        }

        /// <summary>
        /// Builds and signs a licence key.
        /// </summary>
        public static string Build(string privatePath, string customer, string expiry,
                                   List<string> devices, string note)
        {
            if (!File.Exists(privatePath))
            {
                Console.Error.WriteLine("ERROR: the private key is not there: " + privatePath);
                return "";
            }
            return BuildFromXml(File.ReadAllText(privatePath), customer, expiry, devices, note);
        }

        /// <summary>
        /// Builds and signs a licence key, with the private key given as
        /// text.  The test harness uses this one, with a key pair it holds
        /// in memory only - no private key is ever written to a file by the
        /// tests.
        /// </summary>
        public static string BuildFromXml(string privateXml, string customer, string expiry,
                                           List<string> devices, string note)
        {
            if (string.IsNullOrEmpty(privateXml)) return "";

            StringBuilder payload = new StringBuilder();
            payload.AppendLine("v=1");
            payload.AppendLine("customer=" + customer);
            payload.AppendLine("expiry=" + (expiry == null ? "" : expiry));
            payload.AppendLine("dev=" + string.Join(":", devices.ToArray()));
            payload.AppendLine("note=" + (note == null ? "" : note));

            byte[] data = Encoding.UTF8.GetBytes(payload.ToString());
            byte[] signature;
            using (RSACryptoServiceProvider rsa = new RSACryptoServiceProvider())
            {
                rsa.FromXmlString(privateXml);
                signature = rsa.SignData(data, CryptoConfig.MapNameToOID("SHA256"));
            }

            return "WF1." + WfLicense.ToBase64Url(data) + "." + WfLicense.ToBase64Url(signature);
        }
    }
}
