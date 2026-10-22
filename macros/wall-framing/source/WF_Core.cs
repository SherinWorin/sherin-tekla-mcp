// WF_Core.cs - the shared machinery of the wall-framing auto-dimensioner.
//
// This is the PUBLIC part of the macro: the drawing selection, the read of
// the model, the annotation writers that both editions use, and the licence
// check.  It is compiled into WF_Core.dll, which the thin loader
// P142_WF_AutoDimension.cs calls through reflection.
//
// WHAT IS NOT IN HERE: the Pro stages - the top outline, the two overall
// dimensions, the north run, the wall-panel marks, the section marks and
// the text notes.  They live in the Pro module of the owner (WF_Pro.dll),
// which this assembly loads by reflection when the licence allows it.  The
// free edition therefore draws the stud-spacing chains and the band chains
// and nothing else.
//
// The rules of the free edition live in WF_Rules.cs, which has no Tekla
// dependency and can be unit-tested without a running Tekla Structures
// (tests/test_rules.py).
//
// RISK: MEDIUM.  It only creates drawing objects (dimensions / marks /
// texts) inside the active drawing.  It never touches the model.
// Test on a COPY of the target drawing first.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using View = Tekla.Structures.Drawing.View;
using DrawingText = Tekla.Structures.Drawing.Text;

namespace P142.WallFraming
{
    /// <summary>Outcome of a macro run.</summary>
    public sealed class WfResult
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

    /// <summary>The shared machinery of the wall-framing auto-dimensioner.</summary>
    public static class WfCore
    {
        // ------------------------------------------------------------- config
        /// <summary>Geometric tolerance in millimetres.</summary>
        public const double TOL = 0.5;

        /// <summary>User property written on every object the macro creates.</summary>
        public const string TAG_PROP = "P142_WF_AUTODIM";

        /// <summary>Value of the user property, so a re-run can find its own output.</summary>
        public const string TAG_VALUE = "1";

        /// <summary>Assembly-position prefix of the wall panels.</summary>
        public const string PANEL_PREFIX = "BP4-WP";

        /// <summary>Paper distance of a panel chain, in mm.</summary>
        public const double PAPER_PANEL = 8.8;

        /// <summary>Paper distance of a band chain, in mm.</summary>
        public const double PAPER_BAND = 15.9;

        /// <summary>
        /// The title fragments that identify a WALL LAYOUT drawing.  The
        /// macro works on any GA drawing of the model whose title holds one
        /// of them - the mark number does not matter, so the same macro
        /// works on every computer of the project.
        /// </summary>
        public static readonly string[] WALL_LAYOUT_TITLES = {
            "WALL FRAMING", "WALL LAYOUT", "WALL FRAMING LAYOUT"
        };

        /// <summary>Style of the dimensions in the source drawing [50].</summary>
        public static readonly string[] DIM_STYLE = {
            "CombinedDimension.MinimumNumberToCombine=0","CombinedDimension.Format=Off","DimensionType=Relative",
            "ExtensionLine=No","ShortDimension=Outside","ExcludePartsAccordingToFilter=None",
            "Exaggeration.ExaggerationEnabled=False","Arrowhead.ArrowPosition=Both","Arrowhead.Head=NoArrow",
            "Arrowhead.Height=1","Arrowhead.Width=2.5","Color=NewLine6","TrueColor.DrawingColorEnum=NewLine6",
            "TransparentBackground=False","Format.Precision=Whole","Format.Format=NoDecimals","Format.Unit=Automatic",
            "Format.UseDigitGrouping=False","Placing.Placing=Fixed","Placing.Direction.Positive=True",
            "Placing.Direction.Negative=False","Placing.Distance.SearchMargin=1","Placing.Distance.MinimalDistance=15",
            "Placing.Distance.MaximalDistance=0","Text.Font.Color=Black","Text.Font.TrueColor.DrawingColorEnum=Black",
            "Text.Font.Height=2.75","Text.Font.Name=Arial narrow","Text.Font.Italic=False","Text.Font.Bold=False",
            "Text.TextPlacing=AboveDimensionLine" };

        // ------------------------------------------------------------- state
        /// <summary>The log of the run.</summary>
        public static readonly StringBuilder Log = new StringBuilder();

        /// <summary>Number of drawing objects created.</summary>
        public static int Made;
        /// <summary>Number of drawing objects that could not be created.</summary>
        public static int Failed;
        /// <summary>Number of objects that were skipped.</summary>
        public static int Skipped;

        /// <summary>All wall parts read from the model.</summary>
        public static readonly List<WfPart> AllParts = new List<WfPart>();

        /// <summary>The wall panels, grouped from the parts.</summary>
        public static readonly List<WfPanel> Panels = new List<WfPanel>();

        /// <summary>The view the macro draws in.</summary>
        public static View ActiveView;

        /// <summary>The drawing the macro writes into.</summary>
        public static Drawing ActiveDrawing;

        /// <summary>The bounding box of the wall layout: X0, Y0, X1, Y1.</summary>
        public static double Bx0, By0, Bx1, By1;

        // ------------------------------------------------------------- helpers
        /// <summary>Writes a line into the log of the run.</summary>
        public static void Say(string s)
        {
            Log.AppendLine(s);
        }

        // ------------------------------------------------------------- model read
        /// <summary>
        /// Reads the plan-view extents of every wall part of the model, in the
        /// coordinate system of the given view.
        /// </summary>
        public static void ReadWallParts(View view, TSM.Model model)
        {
            AllParts.Clear();
            TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
            TSM.TransformationPlane original = wph.GetCurrentTransformationPlane();
            try
            {
                // the parts are read in the plan view's coordinate system
                wph.SetCurrentTransformationPlane(new TSM.TransformationPlane(view.DisplayCoordinateSystem));
                TSM.ModelObjectSelector sel = model.GetModelObjectSelector();
                foreach (TSM.ModelObject.ModelObjectEnum kind in
                    new[] { TSM.ModelObject.ModelObjectEnum.BEAM, TSM.ModelObject.ModelObjectEnum.CONTOURPLATE })
                {
                    TSM.ModelObjectEnumerator en = sel.GetAllObjectsWithType(kind);
                    while (en.MoveNext())
                    {
                        TSM.Part p = en.Current as TSM.Part;
                        if (p == null) continue;
                        if (!WfRules.IsWallPart(p.Name)) continue;
                        TSM.Solid so = p.GetSolid();
                        if (so == null) continue;
                        string apos = "";
                        p.GetReportProperty("ASSEMBLY_POS", ref apos);

                        WfPart wp = new WfPart();
                        wp.Name = p.Name;
                        wp.Id = p.Identifier.ID;
                        wp.AssemblyPos = apos ?? "";
                        wp.X0 = so.MinimumPoint.X;
                        wp.Y0 = so.MinimumPoint.Y;
                        wp.X1 = so.MaximumPoint.X;
                        wp.Y1 = so.MaximumPoint.Y;
                        AllParts.Add(wp);
                    }
                }
            }
            finally
            {
                wph.SetCurrentTransformationPlane(original);
            }
        }

        // ------------------------------------------------------------- writers
        public static StraightDimensionSetHandler DimHandler;

        /// <summary>
        /// Creates a dimension set.  The points must be given in the order
        /// they appear along the dimension line.
        /// </summary>
        public static void Dim(View view, Drawing dr, string what, List<Point> pts, Vector up, double paperDist)
        {
            if (pts.Count < 2) { Failed++; Say("FAIL " + what + " : fewer than 2 points"); return; }
            PointList pl = new PointList();
            foreach (Point p in pts) pl.Add(p);
            try
            {
                StraightDimensionSet set = DimHandler.CreateDimensionSet(
                    view, pl, up, paperDist * view.Attributes.Scale);
                if (set == null) { Failed++; Say("FAIL " + what + " : CreateDimensionSet returned null"); return; }
                dr.CommitChanges();

                object attr = set.Attributes;
                foreach (string kv in DIM_STYLE)
                {
                    int i = kv.IndexOf('=');
                    try { SetPath(attr, kv.Substring(0, i), kv.Substring(i + 1)); }
                    catch (Exception e) { Say("  style skip " + kv + " : " + e.Message); }
                }
                PropertyInfo ap = FindProp(set.GetType(), "Attributes");
                if (ap != null && ap.CanWrite) ap.SetValue(set, attr, null);
                set.Modify();
                dr.CommitChanges();

                // modifying the style resets the dimension line position
                set.Distance = paperDist * view.Attributes.Scale;
                set.Modify();
                dr.CommitChanges();

                set.SetUserProperty(TAG_PROP, TAG_VALUE);
                Made++;
                StringBuilder sb = new StringBuilder();
                foreach (Point p in pts) sb.AppendFormat("({0:F2},{1:F2}) ", p.X, p.Y);
                Say("OK   dimset " + what + " : " + sb);
            }
            catch (Exception e)
            {
                Failed++;
                Say("FAIL " + what + " : " + e.Message);
            }
        }

        public static PropertyInfo FindProp(Type t, string name)
        {
            PropertyInfo first = null;
            foreach (PropertyInfo pi in t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (pi.Name != name || pi.GetIndexParameters().Length > 0) continue;
                if (pi.DeclaringType == t) return pi;
                if (first == null) first = pi;
            }
            return first;
        }


        /// <summary>Sets a (possibly nested) attribute property by dotted path.</summary>
        public static void SetPath(object target, string path, string value)
        {
            string[] names = path.Split('.');
            object[] chain = new object[names.Length];
            chain[0] = target;
            PropertyInfo[] props = new PropertyInfo[names.Length];
            for (int i = 0; i < names.Length; i++)
            {
                props[i] = FindProp(chain[i].GetType(), names[i]);
                if (props[i] == null) return;
                if (i < names.Length - 1)
                {
                    chain[i + 1] = props[i].GetValue(chain[i], null);
                    if (chain[i + 1] == null) return;
                }
            }
            PropertyInfo last = props[names.Length - 1];
            if (!last.CanWrite) return;
            Type t = last.PropertyType;
            object v;
            if (t.IsEnum) v = Enum.Parse(t, value, true);
            else if (t == typeof(string)) v = value;
            else v = Convert.ChangeType(value, t, CultureInfo.InvariantCulture);
            last.SetValue(chain[names.Length - 1], v, null);
            for (int i = names.Length - 2; i >= 0; i--)
                if (props[i].CanWrite && props[i].PropertyType.IsValueType)
                    props[i].SetValue(chain[i], chain[i + 1], null);
        }


        /// <summary>
        /// Creates a dimension set from a list of stations along one axis.
        /// </summary>
        /// <param name="view">The view to draw in.</param>
        /// <param name="dr">The drawing.</param>
        /// <param name="what">A name for the log.</param>
        /// <param name="stations">The measured coordinates, in order.</param>
        /// <param name="horiz">True when the chain runs along X.</param>
        /// <param name="perp">The fixed coordinate of the dimension line.</param>
        /// <param name="up">The up vector of the dimension line.</param>
        /// <param name="paperDist">The distance of the line from the geometry, in paper mm.</param>
        public static void DimChain(View view, Drawing dr, string what, List<double> stations,
                             bool horiz, double perp, Vector up, double paperDist)
        {
            List<Point> pts = new List<Point>();
            foreach (double v in stations)
            {
                if (horiz) pts.Add(new Point(v, perp, 0));
                else pts.Add(new Point(perp, v, 0));
            }
            Dim(view, dr, what, pts, up, paperDist);
        }


        /// <summary>
        /// Sets the text angle of a mark through MarkBaseAttributes.RotationAngle.
        ///
        /// The angle is written to a copy of the attributes and the copy is
        /// written back, because the getter of Mark.Attributes returns a COPY -
        /// changing it in place has no effect.
        /// </summary>
        public static void SetRotationAngle(Mark mk, int degrees)
        {
            Mark.MarkAttributes attrs = mk.Attributes;
            attrs.RotationAngle = (double)degrees;
            mk.Attributes = attrs;
        }


        /// <summary>
        /// Returns the drawing object that represents the given model part,
        /// or null when the part is not shown in this view.
        /// </summary>
        public static ModelObject FindDrawingPart(View view, int modelId)
        {
            try
            {
                DrawingObjectEnumerator en = view.GetModelObjects(new Identifier(modelId));
                while (en.MoveNext())
                {
                    ModelObject mo = en.Current as ModelObject;
                    if (mo != null) return mo;
                }
            }
            catch (Exception e) { Say("  find part " + modelId + " : " + e.Message); }
            return null;
        }


        /// <summary>
        /// Deletes the objects a previous run of this macro created.
        ///
        /// Every object the macro creates carries the user property
        /// P142_WF_AUTODIM = 1, so a re-run replaces its own output instead of
        /// duplicating it.  The objects are recognised by that property, not
        /// by their position, so hand-made annotations of the target drawing
        /// are never touched.
        /// </summary>
        public static int CleanUp(View view, Drawing dr)
        {
            int removed = 0;
            removed += RemoveTagged<StraightDimensionSet>(view, dr);
            removed += RemoveTagged<SectionMark>(view, dr);
            removed += RemoveTagged<DrawingText>(view, dr);
            removed += RemoveTagged<Mark>(view, dr);
            if (removed > 0)
            {
                dr.CommitChanges();
                Say("previous output removed: " + removed + " objects");
            }
            return removed;
        }


        /// <summary>Deletes every object of the given type that the macro tagged.</summary>
        public static int RemoveTagged<T>(View view, Drawing dr) where T : DrawingObject
        {
            int removed = 0;
            List<T> victims = new List<T>();
            DrawingObjectEnumerator en = view.GetAllObjects(typeof(T));
            while (en.MoveNext())
            {
                T obj = en.Current as T;
                if (obj == null) continue;
                if (!IsTagged(obj)) continue;
                victims.Add(obj);
            }
            foreach (T obj in victims)
            {
                try
                {
                    if (Delete(obj)) removed++;
                }
                catch (Exception e) { Say("  cleanup failed : " + e.Message); }
            }
            return removed;
        }


        /// <summary>True when the object carries the macro's user property.</summary>
        public static bool IsTagged(DrawingObject obj)
        {
            try
            {
                string value = "";
                bool ok = obj.GetUserProperty(TAG_PROP, ref value);
                return ok && value == TAG_VALUE;
            }
            catch { return false; }
        }


        /// <summary>Deletes a drawing object, whatever its type.</summary>
        public static bool Delete(DrawingObject obj)
        {
            if (obj is StraightDimensionSet) return ((StraightDimensionSet)obj).Delete();
            if (obj is SectionMark) return ((SectionMark)obj).Delete();
            if (obj is DrawingText) return ((DrawingText)obj).Delete();
            if (obj is Mark) return ((Mark)obj).Delete();
            return false;
        }


        // -------------------------------------------------- drawing selection
        /// <summary>
        /// True when the drawing is a WALL LAYOUT drawing: a GA drawing whose
        /// title (or name) holds one of the WALL_LAYOUT_TITLES fragments.
        ///
        /// The MARK is never used to recognise a drawing, so the same macro
        /// works on every computer of the project, whatever mark the wall
        /// layout drawing happens to have there.
        /// </summary>
        public static bool IsWallLayout(Drawing d)
        {
            if (d == null) return false;
            string[] texts = { d.Title1, d.Title2, d.Title3, d.Name };
            foreach (string t in texts)
            {
                if (string.IsNullOrEmpty(t)) continue;
                foreach (string f in WALL_LAYOUT_TITLES)
                    if (t.IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            return false;
        }


        /// <summary>
        /// True when the drawing must not be written to: it is frozen or it
        /// has been issued.  The hand-made reference drawing of a project is
        /// always frozen, so this is what keeps the macro away from it.
        /// </summary>
        public static bool IsProtected(Drawing d)
        {
            if (d == null) return true;
            return d.IsFrozen || d.IsIssued || d.IsIssuedButModified;
        }


        /// <summary>
        /// Finds the wall layout drawing the macro works on.
        ///
        /// The ACTIVE drawing wins when it is a wall layout drawing - that is
        /// what the user asked for.  Otherwise the wall layout drawing of
        /// the model is looked up and activated.
        ///
        /// A drawing that is frozen or issued is never chosen: the macro
        /// refuses to touch an issued drawing.
        /// </summary>
        /// <param name="active">The drawing that was active when the macro started.</param>
        /// <param name="note">Explains what was decided, for the log.</param>
        /// <returns>The drawing to dimension, or null when there is none.</returns>
        public static Drawing ResolveTargetDrawing(Drawing active, out string note)
        {
            note = "";
            List<Drawing> candidates = new List<Drawing>();
            try
            {
                DrawingHandler dh = new DrawingHandler();
                DrawingEnumerator de = dh.GetDrawings();
                while (de.MoveNext())
                {
                    Drawing d = de.Current;
                    if (IsWallLayout(d)) candidates.Add(d);
                }
            }
            catch (Exception e)
            {
                note = "the drawings of the model could not be read: " + e.Message;
            }

            // 1. the active drawing, when it is a wall layout drawing
            if (active != null && IsWallLayout(active))
            {
                if (IsProtected(active))
                {
                    note = "the active drawing " + active.Mark +
                           " is issued or frozen - it is not written to";
                    return null;
                }
                note = "the active drawing " + active.Mark + " is the wall layout drawing";
                return active;
            }

            // 2. a wall layout drawing of the model that may be written to
            foreach (Drawing d in candidates)
            {
                if (IsProtected(d)) continue;
                if (active != null && d.Mark == active.Mark) continue;
                note = "the wall layout drawing " + d.Mark + " was activated";
                try
                {
                    DrawingHandler dh = new DrawingHandler();
                    if (!dh.SetActiveDrawing(d))
                    {
                        note = "the wall layout drawing " + d.Mark +
                               " could not be activated";
                        return null;
                    }
                }
                catch (Exception e)
                {
                    note = "the wall layout drawing " + d.Mark +
                           " could not be activated: " + e.Message;
                    return null;
                }
                return d;
            }

            // 3. nothing to work on
            if (candidates.Count > 0)
                note = "every wall layout drawing of the model is issued or frozen";
            else if (active != null)
                note = "the active drawing " + active.Mark +
                       " is not a wall layout drawing, and the model has none";
            else
                note = "no drawing is open";
            return null;
        }


        /// <summary>Creates the band chain of one edge of the layout.</summary>
        public static void DimBand(View view, Drawing dr, string what, WfBandEdge edge,
                            double bx0, double by0, double bx1, double by1)
        {
            List<double> vals = WfRules.BandStations(Panels, edge, bx0, by0, bx1, by1, TOL);
            if (vals.Count < 2) return;

            bool horiz = (edge == WfBandEdge.Bottom || edge == WfBandEdge.Top);
            // the dimension line runs along the edge of the layout
            double line;
            switch (edge)
            {
                case WfBandEdge.Bottom: line = by0; break;
                case WfBandEdge.Top: line = by1; break;
                case WfBandEdge.Left: line = bx0; break;
                default: line = bx1; break;
            }
            // the up vector points away from the layout
            Vector up = horiz ? new Vector(0, -1, 0) : new Vector(-1, 0, 0);
            DimChain(view, dr, what, vals, horiz, line, up, PAPER_BAND);
        }

        // ------------------------------------------------------------ licence
        /// <summary>What the licence says, in one line, for the user.</summary>
        public static string LicenceMessage = "";

        /// <summary>The folder the licence, the state and the Pro module live in.</summary>
        public static string AppFolder()
        {
            return WfLicense.AppFolder();
        }

        /// <summary>The file a customer puts the licence key in.</summary>
        public static string KeyFile()
        {
            return Path.Combine(AppFolder(), "license.txt");
        }

        /// <summary>
        /// Works out which edition this computer may use, and remembers the
        /// run.  Returns 1 for Pro (a licence key or a running trial) and 0
        /// for the free edition.
        ///
        /// A key in the file license.txt is taken into use, so that the
        /// customer can be given one line to paste into a text file.
        /// </summary>
        public static int LicenceEdition()
        {
            string problem = "";
            try
            {
                Dictionary<string, string> state = WfLicense.LoadState();
                string key = "";
                state.TryGetValue("key", out key);

                string file = KeyFile();
                if (File.Exists(file))
                {
                    string text = "";
                    try { text = File.ReadAllText(file).Trim(); } catch { }
                    if (!string.IsNullOrEmpty(text) && text != key)
                    {
                        string message;
                        if (WfLicense.Activate(text, out message)) key = text;
                        else problem = message;
                    }
                }

                WfLicenseInfo info = WfLicense.EvaluateAndRecord(key, DateTime.UtcNow);
                LicenceMessage = (problem == "" ? "" : problem + "  ") + info.Message;
                return (int)info.Edition;
            }
            catch (Exception e)
            {
                LicenceMessage = "The licence could not be read: " + e.Message;
                return 0;
            }
        }

        /// <summary>
        /// Loads the Pro module and lets it draw the Pro stages.
        ///
        /// The module is a separate assembly (WF_Pro.dll) in the app folder.
        /// It is reached through reflection, so that this assembly does not
        /// depend on it: the free edition works without it, and a Pro licence
        /// without the module simply does not get the Pro annotations.
        /// </summary>
        static void RunPro(WfResult result)
        {
            try
            {
                string path = Path.Combine(AppFolder(), "WF_Pro.dll");
                if (!File.Exists(path))
                {
                    result.Message += "  (the Pro module " + path + " is not installed)";
                    return;
                }
                Assembly asm = Assembly.LoadFrom(path);
                Type t = asm.GetType("P142.WallFraming.WfPro");
                if (t == null)
                {
                    result.Message += "  (the Pro module is not the expected one)";
                    return;
                }
                MethodInfo m = t.GetMethod("Execute");
                if (m == null)
                {
                    result.Message += "  (the Pro module has no entry point)";
                    return;
                }
                m.Invoke(null, new object[] { result });
            }
            catch (Exception e)
            {
                result.Message += "  (the Pro module failed: " + e.Message + ")";
            }
        }

        /// <summary>
        /// The macro's core: reads the model, derives the annotation rules and
        /// writes them into the given drawing.  It touches no user interface,
        /// so it can be called from a test.
        /// </summary>
        /// <param name="dr">The target drawing (must be the active drawing).</param>
        /// <param name="result">Receives the outcome of the run.</param>
        public static void Execute(Drawing dr, WfResult result, int edition)
        {
            Log.Length = 0;
            Made = Failed = Skipped = 0;

            if (dr == null)
            {
                result.Ok = false;
                result.Message = "No active drawing.";
                return;
            }

            // ---- find the drawing the macro works on
            //
            // The macro never writes into a drawing it was not asked for:
            // it works on the ACTIVE drawing when that is a wall layout
            // drawing, and on the wall layout drawing of the model
            // otherwise.  A drawing that is issued or frozen is refused.
            string note;
            Drawing target = ResolveTargetDrawing(dr, out note);
            Say(note);
            if (target == null)
            {
                result.Ok = false;
                result.Message = "No wall layout drawing to work on: " + note;
                result.Log = Log.ToString();
                return;
            }
            if (target.Mark != dr.Mark)
            {
                // ResolveTargetDrawing has already activated the drawing.
                dr = target;
            }
            if (IsProtected(dr))
            {
                result.Ok = false;
                result.Message = "The drawing " + dr.Mark +
                                 " is issued or frozen - it is not written to.";
                result.Log = Log.ToString();
                return;
            }

            TSM.Model model = new TSM.Model();

            // ---- pick the plan view (looking straight down)
            View view = null;
            List<View> views = new List<View>();
            DrawingObjectEnumerator ve = dr.GetSheet().GetViews();
            while (ve.MoveNext())
            {
                View v = ve.Current as View;
                if (v != null) views.Add(v);
            }
            foreach (View v in views)
            {
                CoordinateSystem cs = v.DisplayCoordinateSystem;
                if (cs == null) continue;
                if (Math.Abs(cs.AxisX.Z) < 1e-3 && Math.Abs(cs.AxisY.Z) < 1e-3) { view = v; break; }
            }
            if (view == null)
            {
                result.Ok = false;
                result.Message = "No plan view was found in this drawing.";
                result.Log = Log.ToString();
                return;
            }
            ActiveView = view;
            ActiveDrawing = dr;

            Say("Drawing " + dr.Mark + "  view '" + view.Name + "'  scale 1:" + view.Attributes.Scale);

            // ---- remove the output of a previous run of this macro
            CleanUp(view, dr);

            // ---- read the model
            ReadWallParts(view, model);
            Say("wall parts read: " + AllParts.Count);

            // ---- group the parts into wall panels
            //
            // The panel prefix of this project is BP4-WP.  On another
            // computer the model may use another prefix, so when the
            // configured one matches nothing the panels are found
            // automatically (a wall assembly is a group of parts with a
            // bottom track, a top track, a stud and a gypsum board).
            Panels.Clear();
            Panels.AddRange(WfRules.BuildPanels(AllParts, PANEL_PREFIX, TOL));
            if (Panels.Count == 0)
            {
                Panels.AddRange(WfRules.BuildPanelsAuto(AllParts));
                Say("no part matched the prefix '" + PANEL_PREFIX +
                    "' - the wall panels were found automatically");
            }
            Say("wall panels: " + Panels.Count);
            if (Panels.Count == 0)
            {
                result.Ok = false;
                result.Message = "No wall panel was found in the model.";
                result.Log = Log.ToString();
                return;
            }

            DimHandler = new StraightDimensionSetHandler();

            // the bounding box of the whole wall layout
            Bx0 = double.MaxValue; By0 = double.MaxValue;
            Bx1 = double.MinValue; By1 = double.MinValue;
            foreach (WfPart p in AllParts)
            {
                Bx0 = Math.Min(Bx0, p.X0); By0 = Math.Min(By0, p.Y0);
                Bx1 = Math.Max(Bx1, p.X1); By1 = Math.Max(By1, p.Y1);
            }
            if (Bx1 < Bx0) { Bx0 = 0; By0 = 0; Bx1 = 1000; By1 = 1000; }

            // ================================================================
            // 1. DIMENSION SETS
            // ================================================================
            // 1a. the stud-spacing chain of every wall panel
            foreach (WfPanel p in Panels)
            {
                List<double> chain = WfRules.ChainStations(p, TOL);
                if (chain.Count < 2) continue;

                // the dimension line is drawn just outside the panel, with the
                // up vector pointing away from the layout
                bool horiz = p.Horiz;
                double perp = horiz ? p.Bottom.Y0 : p.Bottom.X0;
                Vector up = horiz ? new Vector(0, -1, 0) : new Vector(-1, 0, 0);
                DimChain(view, dr, "bays " + p.Name, chain, horiz, perp, up, PAPER_PANEL);
            }

            // 1b. the band chain of every edge of the layout
            //
            // The TOP edge of the layout is not a band chain: in the source
            // drawing [50] it is the TOP OUTLINE, a staircase that follows
            // the upper boundary of the walls (see 1c).
            DimBand(view, dr, "band bottom", WfBandEdge.Bottom, Bx0, By0, Bx1, By1);
            DimBand(view, dr, "band left", WfBandEdge.Left, Bx0, By0, Bx1, By1);
            DimBand(view, dr, "band right", WfBandEdge.Right, Bx0, By0, Bx1, By1);

            // ================================================================
            // 5. THE PRO STAGES
            // ================================================================
            // The top outline, the two overall dimensions, the north run, the
            // wall-panel marks, the section marks and the text notes are NOT
            // in this assembly.  They live in the Pro module of the owner,
            // which is loaded here only when the licence allows it.
            if (edition == 1) RunPro(result);

            string msg = "Wall framing annotations: " + Made + " created, " +
                         Failed + " failed, " + Skipped + " skipped.";
            Say(msg);
            result.Ok = (Failed == 0);
            result.Made = Made;
            result.Failed = Failed;
            result.Message = msg;
            result.Log = Log.ToString();
        }

    }
}
