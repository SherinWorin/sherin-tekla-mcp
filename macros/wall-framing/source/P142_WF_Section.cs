#pragma warning disable 1633
#pragma reference "Tekla.Macros.Runtime"
#pragma reference "Tekla.Structures"
#pragma reference "Tekla.Structures.Drawing"
#pragma warning restore 1633

// P142_WF_Section.cs - the thin loader for the SECTIONAL ELEVATION macro.
// This is the ONLY file that goes into the Tekla Macro Catalog.
// It loads WF_Section.dll and WF_SectionRules.dll by reflection
// and calls the Execute method.
//
// The macro works on GA drawings with Title2 = "SECTIONAL ELEVATION".
// It creates section views (S1-S6) from section marks on the corresponding
// WALL FRAMING LAYOUT drawing of the same block, adds dimensions, marks,
// standard notes, and level marks.
//
// To install: copy this file to the Tekla Macro folder and run it.
// The DLLs (WF_Section.dll, WF_SectionRules.dll) must be in the same
// directory or in %LOCALAPPDATA%\P142\WallFraming\.
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Tekla.Structures.Drawing;

namespace UserMacros
{
    public sealed class Macro
    {
        [Tekla.Macros.Runtime.MacroEntryPointAttribute()]
        public static void Run(Tekla.Macros.Runtime.IMacroRuntime runtime)
        {
            try
            {
                // ---- locate the assemblies
                string folder = GetAssemblyFolder();
                string corePath = Path.Combine(folder, "WF_Section.dll");
                string rulesPath = Path.Combine(folder, "WF_SectionRules.dll");

                if (!File.Exists(corePath) || !File.Exists(rulesPath))
                {
                    MessageBox.Show("WF_Section: required DLLs not found in " + folder + "\nExpected: WF_Section.dll and WF_SectionRules.dll");
                    return;
                }

                // ---- load assemblies
                // Load FRESH copies every run (from bytes, not LoadFrom): .NET never
                // unloads an assembly, so LoadFrom would keep the first build in
                // memory until Tekla restarts, and would lock the DLL files.
                _folder = folder;
                _loaded.Clear();
                AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                Assembly rulesAsm = LoadFresh(rulesPath);
                Assembly coreAsm = LoadFresh(corePath);

                // ---- get the Execute method
                Type coreType = coreAsm.GetType("P142.WallFraming.WfSectionCore");
                if (coreType == null)
                {
                    MessageBox.Show("WF_Section: type WfSectionCore not found in " + corePath);
                    return;
                }

                // ---- get the result type
                Type resultType = coreAsm.GetType("P142.WallFraming.WfSectionResult");
                if (resultType == null)
                {
                    MessageBox.Show("WF_Section: WfSectionResult type not found in " + corePath);
                    return;
                }

                MethodInfo execute = coreType.GetMethod("Execute",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(Tekla.Structures.Drawing.Drawing), resultType, typeof(bool) },
                    null);

                if (execute == null)
                {
                    // Try alternative signature
                    execute = coreType.GetMethod("Execute",
                        BindingFlags.Public | BindingFlags.Static,
                        null,
                        new[] { typeof(Tekla.Structures.Drawing.Drawing), resultType },
                        null);
                }

                if (execute == null)
                {
                    MessageBox.Show("WF_Section: Execute method not found");
                    return;
                }

                // ---- get the active drawing
                DrawingHandler dh = new DrawingHandler();
                Drawing dr = dh.GetActiveDrawing();
                if (dr == null)
                {
                    MessageBox.Show("WF_Section: no active drawing");
                    return;
                }

                // ---- check if drawing is protected
                MethodInfo isProtectedMethod = coreType.GetMethod("IsProtected",
                    BindingFlags.Public | BindingFlags.Static);
                if (isProtectedMethod != null)
                {
                    bool protected_ = (bool)isProtectedMethod.Invoke(null, new object[] { dr });
                    if (protected_)
                    {
                        MessageBox.Show("WF_Section: drawing " + dr.Mark + " is issued or frozen - not writing to it");
                        return;
                    }
                }

                // ---- create result object and call Execute
                object resultObj = Activator.CreateInstance(resultType);
                execute.Invoke(null, new object[] { dr, resultObj, true });

                // ---- report result
                FieldInfo okField = resultType.GetField("Ok");
                FieldInfo msgField = resultType.GetField("Message");
                FieldInfo logField = resultType.GetField("Log");
                FieldInfo madeField = resultType.GetField("Made");
                FieldInfo failedField = resultType.GetField("Failed");

                if (okField == null)
                {
                    MessageBox.Show("WF_Section: result fields not found in " + resultType.FullName);
                    return;
                }

                bool ok = (bool)okField.GetValue(resultObj);
                string msg = (string)msgField.GetValue(resultObj);
                string log = (string)logField.GetValue(resultObj);
                int made = (int)madeField.GetValue(resultObj);
                int failed = (int)failedField.GetValue(resultObj);

                if (!ok)
                {
                    MessageBox.Show("WF_Section: FAILED (" + failed + " errors)\n" + msg + "\n\nLog:\n" + log);
                }
            }
            catch (Exception e)
            {
                string msg = "WF_Section: EXCEPTION " + e.Message;
                if (e.InnerException != null)
                    msg += "\nInner: " + e.InnerException.Message;
                MessageBox.Show(msg + "\n\nStack Trace:\n" + e.StackTrace);
            }
        }

        /// <summary>
        /// Finds the folder containing the macro assemblies.
        /// Looks next to this script first, then in the app folder.
        /// </summary>
        static string _folder = "";
        static readonly System.Collections.Generic.Dictionary<string, Assembly> _loaded =
            new System.Collections.Generic.Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Loads an assembly from a fresh copy of its bytes (no file lock, no cache).</summary>
        static Assembly LoadFresh(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            Assembly a;
            if (_loaded.TryGetValue(name, out a)) return a;
            a = Assembly.Load(File.ReadAllBytes(path));
            _loaded[name] = a;
            return a;
        }

        /// <summary>Resolves the macro's own WF_* dependencies from the module folder, fresh.</summary>
        static Assembly Resolve(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (!name.StartsWith("WF_", StringComparison.OrdinalIgnoreCase)) return null;
            string path = Path.Combine(_folder, name + ".dll");
            return File.Exists(path) ? LoadFresh(path) : null;
        }

        static string GetAssemblyFolder()
        {
            // 1. Next to this script (when run from macro folder)
            string here = Path.GetDirectoryName(typeof(Macro).Assembly.Location);
            if (!string.IsNullOrEmpty(here))
            {
                string core = Path.Combine(here, "WF_Section.dll");
                if (File.Exists(core)) return here;
            }

            // 2. App folder (%LOCALAPPDATA%\P142\WallFraming\)
            string app = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string appFolder = Path.Combine(app, "P142", "WallFraming");
            string core2 = Path.Combine(appFolder, "WF_Section.dll");
            if (File.Exists(core2)) return appFolder;

            // 3. Fallback to script folder
            return here ?? "";
        }
    }
}
