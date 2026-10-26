#pragma warning disable 1633
#pragma reference "Tekla.Macros.Runtime"
#pragma reference "Tekla.Structures"
#pragma reference "Tekla.Structures.Drawing"
#pragma warning restore 1633

// WallFramingAutoDimensionPro-AIO.cs - the loader of the AIO wall-framing
// auto-dimensioner.
//
// THIS MACRO IS FREE.  It asks for no licence key and it draws EVERY
// annotation of the wall framing layout: the stud-spacing chain of every
// wall panel, the band chains of the layout, the top outline, the two
// overall dimensions, the north run, the wall-panel marks, the section
// marks and the text notes.  It draws what the installed modules provide;
// when the Pro modules are installed next to the free ones, the whole set
// is drawn.
//
// WHAT IS DIFFERENT FROM THE OTHER MACRO
//
// The other macro treats a drawing as ONE wall framing layout.  That is
// right for the drawing of a single block, and wrong for a GA sheet that
// carries the wall framing layout of many blocks: there the bottom band
// chain and the top outline of the "whole sheet" run through every other
// layout, and the drawing shows one long chain instead of one chain per
// framing.
//
// This macro groups the wall panels into the layouts they belong to (the
// block of a panel is the part of its assembly position before the first
// dash, e.g. "BP1-WP3" is a panel of the block "BP1") and draws EVERY
// layout as if it were alone on its own sheet.
//
// The file is deliberately THIN.  It holds no annotation rule and no
// drawing logic: everything is in the assemblies of the namespace
// P142.WallFraming, which are loaded at run time from
//
//     %LOCALAPPDATA%\P142\WallFraming
//
// RISK: MEDIUM.  It only creates drawing objects (dimensions / marks /
// texts) inside the ACTIVE drawing, and it removes the objects of a
// previous run of itself before it draws.  It never touches the model.
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
        /// The macro's core: it asks the module to draw the annotations and
        /// copies the outcome back.  It touches no user interface, so it can
        /// be called from a test.
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
        /// what the macro draws WITHOUT keeping it: the hand-made drawings
        /// of a project are the reference the macro is measured against, and
        /// they must stay as they are.
        /// </summary>
        /// <param name="dr">The target drawing (must be the active drawing).</param>
        /// <param name="result">Receives the outcome of the run.</param>
        /// <param name="save">True to save the drawing that was written to.</param>
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
                // ---- the work itself
                object coreResult = Activator.CreateInstance(resultType);
                MethodInfo work = coreType.GetMethod("ExecuteAio");
                if (work == null || work.GetParameters().Length != 3)
                {
                    // The installed module is an OLDER one than this macro
                    // expects.  Say so, instead of "no entry point" - the
                    // fix is to install the modules again.
                    result.Ok = false;
                    result.Message = "The installed wall framing module is an " +
                        "older version than this macro.  Install the modules " +
                        "again (build.ps1 -Deploy).";
                    return;
                }
                work.Invoke(null, new object[] { dr, coreResult, save });

                result.Ok = (bool)resultType.GetField("Ok").GetValue(coreResult);
                result.Message = (string)resultType.GetField("Message").GetValue(coreResult);
                result.Made = (int)resultType.GetField("Made").GetValue(coreResult);
                result.Failed = (int)resultType.GetField("Failed").GetValue(coreResult);
                result.Log = (string)resultType.GetField("Log").GetValue(coreResult);
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
                MessageBox.Show("Open the GA drawing with the wall framing layouts first.");
                return;
            }
            string markForLog = dr.Mark.Replace("[", "").Replace("]", "").Replace(" ", "_");

            Result result = new Result();
            Execute(dr, result);

            string logPath = "";
            try
            {
                logPath = Path.Combine(LogFolder(), "wfdim_aio_log_" + markForLog + ".txt");
                File.WriteAllText(logPath, result.Log);
            }
            catch { logPath = ""; }

            if (!result.Ok && result.Made == 0)
            {
                MessageBox.Show(result.Message, "P142 wall framing dimensions (AIO)",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MessageBox.Show(result.Message + (logPath == "" ? "" : "\nLog: " + logPath),
                "P142 wall framing dimensions (AIO)");
        }
    }
}
