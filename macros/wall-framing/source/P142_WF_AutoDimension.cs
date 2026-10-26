#pragma warning disable 1633
#pragma reference "Tekla.Macros.Runtime"
#pragma reference "Tekla.Structures"
#pragma reference "Tekla.Structures.Drawing"
#pragma warning restore 1633

// P142_WF_AutoDimension.cs - the loader of the wall-framing auto-dimensioner.
//
// This file is deliberately THIN.  It holds no annotation rule and no
// drawing logic: everything is in the assemblies of the namespace
// P142.WallFraming, which are loaded at run time from
//
//     %LOCALAPPDATA%\P142\WallFraming
//
// The FREE edition draws the stud-spacing chain of every wall panel and the
// band chains of the layout.  The PRO edition draws everything: the top
// outline, the two overall dimensions, the north run, the wall-panel marks,
// the section marks and the text notes.  The Pro part is a separate
// assembly (WF_Pro.dll), which is only installed for a Pro licence.
//
// The LICENCE: the Pro features are free for the first three days.  A
// licence key in %LOCALAPPDATA%\P142\WallFraming\license.txt lifts the
// limit; without one the free edition is used.
//
// RISK: MEDIUM.  It only creates drawing objects (dimensions / marks /
// texts) inside the active drawing.  It never touches the model.
// Test on a COPY of the target drawing first.
using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Tekla.Structures;
using Tekla.Structures.Drawing;

namespace UserMacros
{
    public sealed class Macro
    {
        // ------------------------------------------------------------- outcome
        /// <summary>Outcome of a macro run.</summary>
        public sealed class Result
        {
            /// <summary>True when the macro did everything it was asked to do.</summary>
            public bool Ok;
            /// <summary>Human-readable reason when <see cref="Ok"/> is false.</summary>
            public string Message = "";
            /// <summary>Number of drawing objects created.</summary>
            public int Made;
            /// <summary>Number of drawing objects that could not be created.</summary>
            public int Failed;
            /// <summary>The full log of the run.</summary>
            public string Log = "";
        }

        // ------------------------------------------------------------- the parts
        /// <summary>
        /// The folder the assemblies and the licence live in.
        /// </summary>
        static string AppFolder()
        {
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(Path.Combine(local, "P142"), "WallFraming");
        }

        /// <summary>Loads the core assembly, or returns null and says why.</summary>
        static Assembly LoadCore(out string problem)
        {
            problem = "";
            string path = Path.Combine(AppFolder(), "WF_Core.dll");
            if (!File.Exists(path))
            {
                problem = "The wall framing module is not installed: " + path + " is missing.";
                return null;
            }
            try
            {
                return Assembly.LoadFrom(path);
            }
            catch (Exception e)
            {
                problem = "The wall framing module could not be loaded: " + e.Message;
                return null;
            }
        }

        /// <summary>
        /// The folder the log file is written to: the folder of the macro
        /// DLL, or the temporary folder when that is not writable.
        /// </summary>
        static string LogFolder()
        {
            try
            {
                string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (!string.IsNullOrEmpty(dir) && Directory.Exists(dir)) return dir;
            }
            catch { }
            return Path.GetTempPath();
        }

        // ------------------------------------------------------------- main
        /// <summary>
        /// The macro's core: it asks the module what the licence allows,
        /// lets it write the annotations, and copies the outcome back.
        /// It touches no user interface, so it can be called from a test.
        /// </summary>
        /// <param name="dr">The target drawing (must be the active drawing).</param>
        /// <param name="result">Receives the outcome of the run.</param>
        public static void Execute(Drawing dr, Result result)
        {
            Execute(dr, result, true);
        }

        /// <summary>
        /// The macro's core, with a choice about saving.
        ///
        /// A test that runs the macro on a drawing of the model wants to see
        /// what the macro draws WITHOUT keeping it: the hand-made drawings of
        /// a project are the reference the macro is measured against, and they
        /// must stay as they are.
        /// </summary>
        /// <param name="dr">The target drawing (must be the active drawing).</param>
        /// <param name="result">Receives the outcome of the run.</param>
        /// <param name="save">True to save the drawings that were written to.</param>
        public static void Execute(Drawing dr, Result result, bool save)
        {
            string problem;
            Assembly core = LoadCore(out problem);
            if (core == null)
            {
                result.Ok = false;
                result.Message = problem;
                return;
            }

            Type coreType = core.GetType("P142.WallFraming.WfCore");
            Type resultType = core.GetType("P142.WallFraming.WfResult");
            if (coreType == null || resultType == null)
            {
                result.Ok = false;
                result.Message = "The wall framing module is not the expected one.";
                return;
            }

            try
            {
                // ---- the licence: 1 for Pro (a licence or a running trial),
                //      0 for the free edition
                int edition = (int)coreType.GetMethod("LicenceEdition").Invoke(null, null);
                string licence = (string)coreType.GetField("LicenceMessage").GetValue(null);

                // ---- the work itself
                object coreResult = Activator.CreateInstance(resultType);
                MethodInfo work = null;
                int olderSignature = 0;
                foreach (MethodInfo m in coreType.GetMethods())
                {
                    if (m.Name != "Execute") continue;
                    int n = m.GetParameters().Length;
                    if (n == 4) { work = m; break; }
                    // the three-parameter form is the module of an earlier
                    // version - it has no "save" switch
                    if (n == 3) olderSignature = 3;
                }
                if (work == null && olderSignature == 3)
                {
                    // The installed module is an OLDER one than the loader
                    // expects.  Say so, instead of "no entry point" - the
                    // fix is to install the modules again.
                    result.Ok = false;
                    result.Message = "The installed wall framing module is an " +
                        "older version than this macro.  Install the modules " +
                        "again (build.ps1 -Deploy).";
                    return;
                }
                if (work == null)
                {
                    result.Ok = false;
                    result.Message = "The wall framing module has no entry point.";
                    return;
                }
                work.Invoke(null, new object[] { dr, coreResult, edition, save });

                result.Ok = (bool)resultType.GetField("Ok").GetValue(coreResult);
                result.Message = (string)resultType.GetField("Message").GetValue(coreResult);
                result.Made = (int)resultType.GetField("Made").GetValue(coreResult);
                result.Failed = (int)resultType.GetField("Failed").GetValue(coreResult);
                result.Log = (string)resultType.GetField("Log").GetValue(coreResult);

                if (!string.IsNullOrEmpty(licence))
                    result.Message = licence + "\n" + result.Message;
            }
            catch (TargetInvocationException e)
            {
                result.Ok = false;
                result.Message = "The wall framing module failed: " +
                    (e.InnerException == null ? e.Message : e.InnerException.Message);
            }
            catch (Exception e)
            {
                result.Ok = false;
                result.Message = "The wall framing module failed: " + e.Message;
            }
        }

        /// <summary>The macro's user-interface entry point.</summary>
        [Tekla.Macros.Runtime.MacroEntryPointAttribute()]
        public static void Run(Tekla.Macros.Runtime.IMacroRuntime runtime)
        {
            DrawingHandler dh = new DrawingHandler();
            Drawing dr = dh.GetActiveDrawing();
            if (dr == null)
            {
                MessageBox.Show("Open the WALL FRAMING LAYOUT GA drawing first.");
                return;
            }
            string markForLog = dr.Mark.Replace("[", "").Replace("]", "").Replace(" ", "_");

            Result result = new Result();
            Execute(dr, result);

            // The macro may have activated another drawing, so the drawing it
            // really worked on is the active one now.
            Drawing done = dh.GetActiveDrawing();
            if (done != null)
            {
                markForLog = done.Mark.Replace("[", "").Replace("]", "").Replace(" ", "_");
                try
                {
                    // CommitChanges() alone does not persist the objects
                    // (API trap 1): the drawing has to be saved.
                    dh.SaveActiveDrawing();
                }
                catch (Exception e)
                {
                    result.Message += "\nThe drawing could not be saved: " + e.Message;
                }
            }

            string logPath = "";
            try
            {
                logPath = Path.Combine(LogFolder(), "wfdim_log_" + markForLog + ".txt");
                File.WriteAllText(logPath, result.Log);
            }
            catch { logPath = ""; }

            if (!result.Ok && result.Made == 0)
            {
                MessageBox.Show(result.Message, "P142 wall framing dimensions",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MessageBox.Show(result.Message + (logPath == "" ? "" : "\nLog: " + logPath),
                "P142 wall framing dimensions");
        }
    }
}
