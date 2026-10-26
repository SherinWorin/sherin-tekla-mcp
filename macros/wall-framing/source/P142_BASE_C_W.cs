#pragma warning disable 1633
#pragma reference "Tekla.Macros.Runtime"
#pragma reference "Tekla.Structures"
#pragma reference "Tekla.Structures.Drawing"
#pragma warning restore 1633

// P142_BASE_C_W.cs - the thin loader of the BASE, CEILING, WALL drawing macro.
// This is the ONLY file that goes into the Tekla macro catalog (drawings).
// It loads WF_BaseCW.dll (and WF_Rules.dll) from %LOCALAPPDATA%\P142\WallFraming
// FRESH on every run (from bytes - a rebuild is picked up without restarting
// Tekla) and calls WfBaseCwCore.Execute on the open drawing.
// Open the EMPTY BASE, CEILING, WALL drawing of a pod (e.g. BP20M-10001) and run it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Tekla.Structures.Drawing;

namespace UserMacros
{
    public sealed class Macro
    {
        static string _folder = "";
        static readonly Dictionary<string, Assembly> _loaded = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        [Tekla.Macros.Runtime.MacroEntryPointAttribute()]
        public static void Run(Tekla.Macros.Runtime.IMacroRuntime runtime)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "P142", "WallFraming");
                string corePath = Path.Combine(folder, "WF_BaseCW.dll");
                if (!File.Exists(corePath))
                {
                    MessageBox.Show("P142_BASE_C_W: WF_BaseCW.dll not found in " + folder);
                    return;
                }
                _folder = folder;
                _loaded.Clear();
                AppDomain.CurrentDomain.AssemblyResolve -= Resolve;
                AppDomain.CurrentDomain.AssemblyResolve += Resolve;
                Assembly core = LoadFresh(corePath);

                Type coreType = core.GetType("P142.WallFraming.WfBaseCwCore");
                Type resultType = core.GetType("P142.WallFraming.WfBaseCwResult");
                if (coreType == null || resultType == null)
                {
                    MessageBox.Show("P142_BASE_C_W: WfBaseCwCore / WfBaseCwResult not found in " + corePath);
                    return;
                }
                MethodInfo execute = coreType.GetMethod("Execute", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(Drawing), resultType, typeof(bool) }, null);
                if (execute == null) { MessageBox.Show("P142_BASE_C_W: Execute not found"); return; }

                Drawing dr = new DrawingHandler().GetActiveDrawing();
                if (dr == null) { MessageBox.Show("P142_BASE_C_W: open the BASE, CEILING, WALL drawing first"); return; }

                object res = Activator.CreateInstance(resultType);
                execute.Invoke(null, new object[] { dr, res, true });

                bool ok = (bool)resultType.GetField("Ok").GetValue(res);
                if (!ok)
                {
                    int failed = (int)resultType.GetField("Failed").GetValue(res);
                    string msg = (string)resultType.GetField("Message").GetValue(res);
                    string log = (string)resultType.GetField("Log").GetValue(res);
                    MessageBox.Show((failed > 0 ? "P142_BASE_C_W: FAILED (" + failed + ")\n" : "P142_BASE_C_W: nothing done\n") +
                                    msg + "\n\nLog:\n" + (log.Length > 6000 ? log.Substring(0, 6000) + "\n..." : log));
                }
            }
            catch (Exception e)
            {
                string msg = "P142_BASE_C_W: EXCEPTION " + e.Message;
                if (e.InnerException != null) msg += "\nInner: " + e.InnerException.Message;
                MessageBox.Show(msg + "\n\nStack Trace:\n" + e.StackTrace);
            }
        }

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
    }
}
