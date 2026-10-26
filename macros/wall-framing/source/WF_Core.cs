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

        /// <summary>
        /// Configuration settings loaded from WF_Settings.json or defaults.
        /// Loaded once on first access.
        /// </summary>
        static WfSettings _settings;
        public static WfSettings Settings
        {
            get
            {
                if (_settings == null)
                    _settings = WfSettingsLoader.Load();
                return _settings;
            }
        }

        /// <summary>Assembly-position prefix of the wall panels.</summary>
        [Obsolete("Use Settings.PanelPrefix instead")]
        public const string PANEL_PREFIX = "BP4-WP";

        /// <summary>Paper distance of a panel chain, in mm.</summary>
        [Obsolete("Use Settings.PaperDistances.Panel instead")]
        public const double PAPER_PANEL = 8.8;

        /// <summary>Paper distance of a band chain, in mm.</summary>
        [Obsolete("Use Settings.PaperDistances.Band instead")]
        public const double PAPER_BAND = 15.9;

        /// <summary>
        /// The title fragments that identify a WALL LAYOUT drawing.  The
        /// macro works on any GA drawing of the model whose title holds one
        /// of them - the mark number does not matter, so the same macro
        /// works on every computer of the project.
        /// </summary>
        [Obsolete("Use Settings.WallLayoutTitles instead")]
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

        /// <summary>
        /// True when a note about the Pro module was already written into
        /// the message of the run - the AIO macro calls the Pro module once
        /// per layout, and the note is only needed once.
        /// </summary>
        static bool ProNoteSaid;

        /// <summary>
        /// The number of wall framing layouts of the last AIO run - a sheet
        /// with many layouts is dimensioned layout by layout.
        /// </summary>
        public static int Layouts;

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

        /// <summary>
        /// The assembly of every part of the model, by the id of the part.
        ///
        /// A GA drawing shows only the assemblies of its own block, so the
        /// drawing is what tells the macro which walls to dimension.  The
        /// drawing gives the id of the MODEL part; this map turns it into the
        /// id of the assembly.
        /// </summary>
        static readonly Dictionary<int, int> PartAssembly = new Dictionary<int, int>();

        /// <summary>
        /// The coordinate system the model was read in, or null when the model
        /// has not been read yet.
        ///
        /// The drawings of a model share their plan view coordinate system
        /// almost always, so the model is read ONCE for the whole run.
        /// </summary>
        static string ReadCoordinateSystem = null;

        // ------------------------------------------------------------- helpers
        /// <summary>Writes a line into the log of the run.</summary>
        public static void Say(string s)
        {
            Log.AppendLine(s);
        }

        // ------------------------------------------------------------- model read
        /// <summary>
        /// A text that tells two coordinate systems apart.
        /// </summary>
        static string CsSignature(CoordinateSystem cs)
        {
            if (cs == null) return "";
            return string.Format(CultureInfo.InvariantCulture, "{0:F3}|{1:F3}|{2:F3}|{3:F3}|{4:F3}|{5:F3}|{6:F3}|{7:F3}|{8:F3}",
                cs.Origin.X, cs.Origin.Y, cs.Origin.Z,
                cs.AxisX.X, cs.AxisX.Y, cs.AxisX.Z,
                cs.AxisY.X, cs.AxisY.Y, cs.AxisY.Z);
        }

        /// <summary>
        /// Reads the plan-view extents of every wall part of the model, in the
        /// coordinate system of the given view.
        ///
        /// The ASSEMBLY_POS is read from the ASSEMBLY and not from the part,
        /// because a model can leave the position off its parts: the position
        /// of the assembly is then the only place it is written down.  The
        /// position of the part is used when the assembly has none, so the
        /// models that write it on the parts keep working.
        ///
        /// The read is skipped when the model was already read in the same
        /// coordinate system, which is the case for every drawing of a model.
        /// </summary>
        public static void ReadWallParts(View view, TSM.Model model)
        {
            CoordinateSystem cs = view.DisplayCoordinateSystem;
            string signature = CsSignature(cs);
            if (ReadCoordinateSystem == signature) return;
            ReadCoordinateSystem = signature;

            AllParts.Clear();
            PartAssembly.Clear();

            TSM.WorkPlaneHandler wph = model.GetWorkPlaneHandler();
            TSM.TransformationPlane original = wph.GetCurrentTransformationPlane();
            try
            {
                // the parts are read in the plan view's coordinate system
                wph.SetCurrentTransformationPlane(new TSM.TransformationPlane(cs));
                TSM.ModelObjectSelector sel = model.GetModelObjectSelector();
                foreach (TSM.ModelObject.ModelObjectEnum kind in
                    new[] { TSM.ModelObject.ModelObjectEnum.BEAM, TSM.ModelObject.ModelObjectEnum.CONTOURPLATE })
                {
                    TSM.ModelObjectEnumerator en = sel.GetAllObjectsWithType(kind);
                    while (en.MoveNext())
                    {
                        TSM.Part p = en.Current as TSM.Part;
                        if (p == null) continue;

                        TSM.Assembly asm = null;
                        try { asm = p.GetAssembly(); } catch (Exception) { }
                        if (asm == null) continue;
                        int assemblyId = asm.Identifier.ID;
                        PartAssembly[p.Identifier.ID] = assemblyId;

                        if (!WfRules.IsWallPart(p.Name)) continue;
                        TSM.Solid so = p.GetSolid();
                        if (so == null) continue;

                        // the position of the assembly, or the one of the part
                        string apos = "";
                        asm.GetReportProperty("ASSEMBLY_POS", ref apos);
                        if (string.IsNullOrEmpty(apos))
                        {
                            apos = "";
                            p.GetReportProperty("ASSEMBLY_POS", ref apos);
                        }

                        WfPart wp = new WfPart();
                        wp.Name = p.Name;
                        wp.Id = p.Identifier.ID;
                        wp.AssemblyPos = apos ?? "";
                        wp.Key = assemblyId.ToString(CultureInfo.InvariantCulture);
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

        /// <summary>
        /// The keys of the assemblies the given view shows.
        ///
        /// This is how the macro knows which walls belong to the drawing: a
        /// GA drawing of one block of a model shows the assemblies of that
        /// block and of no other.
        /// </summary>
        public static HashSet<string> ShownAssemblies(View view)
        {
            HashSet<string> keys = new HashSet<string>(StringComparer.Ordinal);
            DrawingObjectEnumerator en = view.GetAllObjects();
            while (en.MoveNext())
            {
                DrawingObject o = en.Current;
                if (o == null) continue;
                ModelObject mo = o as ModelObject;
                if (mo == null) continue;
                int id;
                try { id = mo.ModelIdentifier.ID; } catch (Exception) { continue; }
                if (id == 0) continue;
                int assemblyId;
                if (!PartAssembly.TryGetValue(id, out assemblyId)) continue;
                keys.Add(assemblyId.ToString(CultureInfo.InvariantCulture));
            }
            return keys;
        }

        /// <summary>
        /// The wall panels of the given view, in the given parts.
        ///
        /// The panels are the wall assemblies the view shows.  When the view
        /// shows none - a drawing whose view is empty, or a model whose
        /// assemblies are not shown in the drawing - the panels are found the
        /// old way: by the name of the assembly, and then by their
        /// construction.
        /// </summary>
        public static void SelectPanels(View view)
        {
            Panels.Clear();

            HashSet<string> shown = ShownAssemblies(view);
            if (shown.Count > 0)
            {
                List<WfPart> mine = new List<WfPart>();
                foreach (WfPart p in AllParts)
                    if (shown.Contains(p.Key)) mine.Add(p);
                if (mine.Count > 0)
                {
                    Panels.AddRange(WfRules.BuildPanelsForAssemblies(mine, shown, TOL));
                    Say("the view shows " + shown.Count + " assemblies, " +
                        mine.Count + " wall parts, " + Panels.Count + " wall panels");
                    return;
                }
            }

            // no fallback: the view shows nothing that the macro can read
            Say("the view shows no wall assembly - the wall panels were found " +
                "by their name and by their construction instead");
            Panels.AddRange(WfRules.BuildPanels(AllParts, Settings.PanelPrefix, TOL));
            if (Panels.Count == 0)
            {
                Panels.AddRange(WfRules.BuildPanelsAuto(AllParts));
                Say("no part matched the prefix '" + Settings.PanelPrefix +
                    "' - the wall panels were found automatically");
            }
        }

        // ------------------------------------------------------------- writers
        public static StraightDimensionSetHandler DimHandler;

        /// <summary>
        /// Creates a dimension set.  The points must be given in the order
        /// they appear along the dimension line.
        /// </summary>
        /// <summary>
        /// True while P142_BASE_C_W dimensions its wall plan: every dimension
        /// point is moved onto the first wall face inward from it, so the
        /// extension lines start at the walls and not at the layout's bounding
        /// box (an L-shaped pod, BP20M 2026-10-26).  False for the wall layout
        /// macros, which are unchanged.
        /// </summary>
        public static bool SnapToWalls;

        /// <summary>Moves each point onto the first wall face inward from it (see SnapToWalls).</summary>
        public static List<Point> SnapPoints(List<Point> pts, Vector up)
        {
            var res = new List<Point>();
            bool alongX = Math.Abs(up.Y) > 0.5;
            double sg = alongX ? Math.Sign(up.Y) : Math.Sign(up.X);
            foreach (Point p in pts)
            {
                double best = double.NaN;
                foreach (WfPart w in AllParts)
                {
                    if (!string.Equals(w.Name, "BOTTOM_TRACK", StringComparison.OrdinalIgnoreCase)) continue;   // owner: from the base track edge
                    double a = alongX ? p.X : p.Y, lo = alongX ? w.X0 : w.Y0, hi = alongX ? w.X1 : w.Y1;
                    if (a < lo - 1 || a > hi + 1) continue;
                    // the face that looks in the up direction, at or inward of the point
                    double face = alongX ? (sg < 0 ? w.Y0 : w.Y1) : (sg < 0 ? w.X0 : w.X1);
                    double here = alongX ? p.Y : p.X;
                    if (sg * (face - here) > 1) continue;
                    if (double.IsNaN(best) || sg * (face - best) > 0) best = face;
                }
                res.Add(double.IsNaN(best) ? p : (alongX ? new Point(p.X, best, 0) : new Point(best, p.Y, 0)));
            }
            return res;
        }

        public static void Dim(View view, Drawing dr, string what, List<Point> pts, Vector up, double paperDist)
        {
            if (pts.Count < 2) { Failed++; Say("FAIL " + what + " : fewer than 2 points"); return; }
            if (SnapToWalls) pts = SnapPoints(pts, up);
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
            return CleanUp(view, dr, false);
        }


        /// <summary>
        /// Deletes the objects a previous run of this macro created.
        ///
        /// The section marks can be kept:  a sheet that carries many wall
        /// framing layouts belongs to the client, and the section lines of
        /// the client carry the tag of the macro as well - a drawing that
        /// was put together from the drawings of the macro keeps the tag of
        /// the objects it was copied from.  Deleting them would take the
        /// section lines of the client with them, so the AIO macro keeps
        /// the section marks of the sheet and only draws the ones that a
        /// layout does not have yet.
        /// </summary>
        /// <param name="view">The view the macro draws in.</param>
        /// <param name="dr">The drawing that is written to.</param>
        /// <param name="keepSections">
        /// True to keep the section marks of the drawing.
        /// </param>
        public static int CleanUp(View view, Drawing dr, bool keepSections)
        {
            int removed = 0;
            removed += RemoveTagged<StraightDimensionSet>(view, dr);
            if (!keepSections) removed += RemoveTagged<SectionMark>(view, dr);
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
                foreach (string f in Settings.WallLayoutTitles)
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
        /// Finds the wall layout drawings the macro works on.
        ///
        /// The ACTIVE drawing wins when it is a wall layout drawing - that is
        /// what the user asked for, and only that drawing is worked on.
        /// Otherwise EVERY wall layout drawing of the model that may be
        /// written to is worked on, one after the other: a model has one
        /// wall layout drawing per block, and the macro dimensions them all.
        ///
        /// A drawing that is frozen or issued is never chosen: the macro
        /// refuses to touch an issued drawing.
        /// </summary>
        /// <param name="active">The drawing that was active when the macro started.</param>
        /// <param name="note">Explains what was decided, for the log.</param>
        /// <returns>The drawings to dimension; the list is empty when there is none.</returns>
        public static List<Drawing> ResolveTargetDrawings(Drawing active, out string note)
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
                    return new List<Drawing>();
                }
                note = "the active drawing " + active.Mark + " is the wall layout drawing";
                return new List<Drawing> { active };
            }

            // 2. every wall layout drawing of the model that may be written to
            List<Drawing> targets = new List<Drawing>();
            foreach (Drawing d in candidates)
            {
                if (IsProtected(d)) continue;
                if (active != null && d.Mark == active.Mark) continue;
                targets.Add(d);
            }
            targets.Sort(delegate (Drawing a, Drawing b)
            {
                int c = string.Compare(a.Name ?? "", b.Name ?? "", StringComparison.OrdinalIgnoreCase);
                if (c != 0) return c;
                return string.Compare(a.Mark ?? "", b.Mark ?? "", StringComparison.OrdinalIgnoreCase);
            });
            if (targets.Count > 0)
            {
                note = "the active drawing is not a wall layout drawing - " +
                       targets.Count + " wall layout drawings of the model are worked on";
                return targets;
            }

            // 3. nothing to work on
            if (candidates.Count > 0)
                note = "every wall layout drawing of the model is issued or frozen";
            else if (active != null)
                note = "the active drawing " + active.Mark +
                       " is not a wall layout drawing, and the model has none";
            else
                note = "no drawing is open";
            return new List<Drawing>();
        }


        /// <summary>
        /// The plan view of a drawing: the view that looks straight down.
        ///
        /// The view is found by its COORDINATE SYSTEM and not by its place
        /// in the list: the first view of a drawing is not always the plan
        /// view (see HANDOVER/04-TEKLA-2025-API-TRAPS.md, trap 18).
        /// </summary>
        /// <returns>The plan view, or null when the drawing has none.</returns>
        static View PlanView(Drawing dr)
        {
            if (dr == null) return null;
            List<View> views = new List<View>();
            try
            {
                DrawingObjectEnumerator ve = dr.GetSheet().GetViews();
                while (ve.MoveNext())
                {
                    View v = ve.Current as View;
                    if (v != null) views.Add(v);
                }
            }
            catch { return null; }

            foreach (View v in views)
            {
                CoordinateSystem cs = v.DisplayCoordinateSystem;
                if (cs == null) continue;
                if (Math.Abs(cs.AxisX.Z) < 1e-3 && Math.Abs(cs.AxisY.Z) < 1e-3) return v;
            }
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
            DimChain(view, dr, what, vals, horiz, line, up, Settings.PaperDistances.Band);
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
        /// Lets the Pro module draw the Pro stages of the whole drawing.
        /// </summary>
        static void RunPro(WfResult result)
        {
            CallPro("Execute", result, new object[] { result });
        }

        /// <summary>
        /// Lets the Pro module draw the stages of ONE wall framing layout.
        ///
        /// The module is called once per layout, and it is told which block
        /// the layout is: the section marks of a layout are the ones of that
        /// block, and they are copied from the drawing of that block.
        /// </summary>
        /// <param name="result">Receives the outcome of the run.</param>
        /// <param name="block">The block of the layout, e.g. "BP1".</param>
        static void RunProLayout(WfResult result, string block)
        {
            CallPro("ExecuteLayout", result, new object[] { result, block });
        }

        /// <summary>
        /// Loads the Pro module and calls one of its entry points.
        ///
        /// The module is a separate assembly (WF_Pro.dll) in the app folder.
        /// It is reached through reflection, so that this assembly does not
        /// depend on it: the free edition works without it, and a Pro licence
        /// without the module simply does not get the Pro annotations.
        ///
        /// The module is built against the rules assembly, and the two have
        /// to be of the same version: the module calls the rules by their
        /// exact names, and a rules assembly of a different version turns
        /// every call into a bare "Method not found" - which says nothing
        /// about the cause.  So the version the module was built for is
        /// read first and compared with the version of the rules that are
        /// installed.  A mismatch is reported as such, and the Pro stages
        /// are left out rather than failing one by one.
        /// </summary>
        /// <param name="entry">The name of the entry point of the module.</param>
        /// <param name="result">Receives the outcome of the run.</param>
        /// <param name="args">The arguments of the entry point.</param>
        /// <returns>True when the module was called.</returns>
        static Assembly _proAsm;
        static string _proFolder = "";

        /// <summary>
        /// Loads the Pro module so that it binds to THIS WF_Core (and its rules
        /// and licence assemblies), whatever way this WF_Core was loaded.
        ///
        /// Assembly.LoadFrom would let the module bind to a second copy of
        /// WF_Core.dll from the folder; that copy has its own (empty) static
        /// state - the panels, the view, the drawing - so the Pro stages would
        /// draw nothing when this WF_Core was loaded from bytes (P142_BASE_C_W).
        /// The module is loaded from its bytes and its WF_* references are
        /// answered with the assemblies already in use.
        /// </summary>
        static Assembly LoadPro(string path)
        {
            if (_proAsm != null) return _proAsm;
            _proFolder = Path.GetDirectoryName(path);
            AppDomain.CurrentDomain.AssemblyResolve -= ResolveForPro;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveForPro;
            _proAsm = Assembly.Load(File.ReadAllBytes(path));
            return _proAsm;
        }

        static readonly Dictionary<string, Assembly> _proDeps = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);

        static Assembly ResolveForPro(object sender, ResolveEventArgs args)
        {
            // answer ONLY the Pro module and its own dependencies: a handler of an
            // earlier run stays registered and must not answer a later run
            Assembly req = args.RequestingAssembly;
            if (req == null || _proAsm == null || (req != _proAsm && !_proDeps.ContainsValue(req))) return null;
            string name = new AssemblyName(args.Name).Name;
            if (string.Equals(name, "WF_Core", StringComparison.OrdinalIgnoreCase)) return typeof(WfCore).Assembly;
            if (string.Equals(name, "WF_Rules", StringComparison.OrdinalIgnoreCase)) return typeof(WfRules).Assembly;
            if (string.Equals(name, "WF_License", StringComparison.OrdinalIgnoreCase)) return typeof(WfLicense).Assembly;
            if (!name.StartsWith("WF_", StringComparison.OrdinalIgnoreCase)) return null;
            Assembly a;
            if (_proDeps.TryGetValue(name, out a)) return a;
            string f = Path.Combine(_proFolder, name + ".dll");
            if (!File.Exists(f)) return null;
            a = Assembly.Load(File.ReadAllBytes(f));
            _proDeps[name] = a;
            return a;
        }

        static bool CallPro(string entry, WfResult result, object[] args)
        {
            try
            {
                string path = ProModulePath();
                if (path == null)
                {
                    ProNote(result, "the Pro module is not installed");
                    return false;
                }
                Assembly asm = LoadPro(path);
                Type t = asm.GetType("P142.WallFraming.WfPro");
                if (t == null)
                {
                    ProNote(result, "the Pro module is not the expected one");
                    return false;
                }
                MethodInfo m = t.GetMethod(entry);
                if (m == null)
                {
                    ProNote(result, "the Pro module has no entry point '" +
                        entry + "' - install the modules again");
                    return false;
                }

                // ---- the version handshake
                int wanted = ProApiVersion(t);
                if (wanted < 0)
                {
                    ProNote(result, "the Pro module is older than the " +
                        "rules - install the modules again, they are of " +
                        "different versions");
                    return false;
                }
                int installed = InstalledRulesApiVersion();
                if (installed < 0)
                {
                    ProNote(result, "the installed rules assembly is " +
                        "older than this macro - install the modules again");
                    return false;
                }
                if (wanted != installed)
                {
                    ProNote(result, "the Pro module was built for version " +
                        wanted + " of the rules, and the installed rules are " +
                        "version " + installed + " - install the modules " +
                        "again, they are of different versions");
                    return false;
                }

                m.Invoke(null, args);
                return true;
            }
            catch (Exception e)
            {
                ProNote(result, "the Pro module failed: " + e.Message);
                return false;
            }
        }


        /// <summary>
        /// Writes a note about the Pro module into the message of the run -
        /// but only the first one.
        ///
        /// The AIO macro calls the Pro module once per wall framing layout,
        /// and a module that is not installed would say the same thing
        /// nineteen times.
        /// </summary>
        static void ProNote(WfResult result, string text)
        {
            if (ProNoteSaid) return;
            ProNoteSaid = true;
            result.Message += "  (" + text + ")";
        }

        /// <summary>
        /// Where the Pro module is looked for.
        ///
        /// The module is looked for NEXT TO THIS ASSEMBLY first, and in the
        /// app folder second.  The modules belong together: the Pro module
        /// is built against the rules assembly, and a Pro module from
        /// another build cannot work with the rules of this one.  Looking
        /// beside this assembly keeps a folder self-consistent - a build
        /// that is tested from its own folder cannot pick up the module of
        /// an older installation by accident.
        ///
        /// Returns null when the module is nowhere to be found.
        /// </summary>
        static string ProModulePath()
        {
            List<string> folders = new List<string>();
            try
            {
                string here = Path.GetDirectoryName(
                    typeof(WfCore).Assembly.Location);
                if (!string.IsNullOrEmpty(here)) folders.Add(here);
            }
            catch { }
            string app = AppFolder();
            if (!folders.Contains(app)) folders.Add(app);

            foreach (string folder in folders)
            {
                string path = Path.Combine(folder, "WF_Pro.dll");
                if (File.Exists(path)) return path;
            }
            return null;
        }

        /// <summary>
        /// The version of the rules assembly that is LOADED at run time.
        ///
        /// This is read through reflection, and not taken from the constant
        /// of this file: a constant is fixed when this assembly is
        /// compiled, so it would report the version the rules had when the
        /// core was built, and not the version that is actually on the
        /// disk.  The number that matters is the one of the rules assembly
        /// that the Pro module will be called against.
        ///
        /// Returns -1 when the loaded rules assembly carries no version at
        /// all: it is an old one.
        /// </summary>
        static int InstalledRulesApiVersion()
        {
            try
            {
                Assembly rules = typeof(WfPanel).Assembly;
                Type t = rules.GetType("P142.WallFraming.WfRules");
                if (t == null) return -1;
                FieldInfo f = t.GetField("ApiVersion",
                    BindingFlags.Public | BindingFlags.Static);
                if (f == null) return -1;
                object v = f.GetRawConstantValue();
                if (v == null) return -1;
                return Convert.ToInt32(v);
            }
            catch { return -1; }
        }

        /// <summary>
        /// The version of the rules the Pro module was built against.
        ///
        /// The module carries it as a constant, so that a module of a
        /// different version can be recognised before it is called.  A
        /// module that does not carry the number at all is an old one, and
        /// -1 is returned for it: it is not called, because it would fail
        /// on every single object it touches.
        /// </summary>
        static int ProApiVersion(Type proType)
        {
            try
            {
                FieldInfo f = proType.GetField("RulesApiVersion",
                    BindingFlags.Public | BindingFlags.Static);
                if (f == null) return -1;
                object v = f.GetRawConstantValue();
                if (v == null) return -1;
                return Convert.ToInt32(v);
            }
            catch { return -1; }
        }

        /// <summary>
        /// The macro's core: reads the model, derives the annotation rules and
        /// writes them into the drawings of the model that show a wall
        /// layout.  It touches no user interface, so it can be called from a
        /// test.
        /// </summary>
        /// <param name="dr">The drawing that was active when the macro started.</param>
        /// <param name="result">Receives the outcome of the run.</param>
        public static void Execute(Drawing dr, WfResult result, int edition)
        {
            Execute(dr, result, edition, true);
        }

        /// <summary>
        /// The macro's core, with a choice about saving.
        ///
        /// A test that runs the macro on a drawing of the model wants to see
        /// what the macro draws WITHOUT keeping it: the hand-made drawings of
        /// a project are the reference the macro is measured against, and they
        /// must stay as they are.
        /// </summary>
        /// <param name="dr">The drawing that was active when the macro started.</param>
        /// <param name="result">Receives the outcome of the run.</param>
        /// <param name="edition">1 for Pro, 0 for the free edition.</param>
        /// <param name="save">True to save the drawings that were written to.</param>
        public static void Execute(Drawing dr, WfResult result, int edition, bool save)
        {
            Log.Length = 0;
            Made = Failed = Skipped = 0;
            ProNoteSaid = false;
            Layouts = 0;
            ReadCoordinateSystem = null;      // a new run reads the model again

            if (dr == null)
            {
                result.Ok = false;
                result.Message = "No active drawing.";
                result.Log = Log.ToString();
                return;
            }

            // ---- find the drawings the macro works on
            //
            // The macro never writes into a drawing it was not asked for:
            // it works on the ACTIVE drawing when that is a wall layout
            // drawing, and on every wall layout drawing of the model
            // otherwise.  A drawing that is issued or frozen is refused.
            string note;
            List<Drawing> targets = ResolveTargetDrawings(dr, out note);
            Say(note);
            if (targets.Count == 0)
            {
                result.Ok = false;
                result.Message = "No wall layout drawing to work on: " + note;
                result.Log = Log.ToString();
                return;
            }

            TSM.Model model = new TSM.Model();
            DrawingHandler dh = new DrawingHandler();
            DimHandler = new StraightDimensionSetHandler();

            int done = 0;
            List<string> summary = new List<string>();

            foreach (Drawing target in targets)
            {
                // the drawing has to be the active one: the drawing API only
                // writes into the drawing that is open
                if (target.Mark != dr.Mark)
                {
                    try
                    {
                        if (!dh.SetActiveDrawing(target))
                        {
                            Say("the drawing " + target.Mark +
                                " could not be activated - it is left alone");
                            continue;
                        }
                    }
                    catch (Exception e)
                    {
                        Say("the drawing " + target.Mark +
                            " could not be activated: " + e.Message);
                        continue;
                    }
                }

                Drawing working = dh.GetActiveDrawing();
                if (working == null) working = target;
                if (IsProtected(working))
                {
                    Say("the drawing " + working.Mark +
                        " is issued or frozen - it is not written to");
                    continue;
                }

                Say("");
                Say("=== drawing " + working.Mark + "  (" + working.Name + ") ===");

                int madeBefore = Made, failedBefore = Failed;
                DimensionDrawing(working, model, result, edition);
                summary.Add(working.Mark + ": " + (Made - madeBefore) + " created, " +
                            (Failed - failedBefore) + " failed");
                done++;

                // CommitChanges() alone does not persist the objects (API
                // trap 1): the drawing has to be saved.
                if (save)
                {
                    try
                    {
                        dh.SaveActiveDrawing();
                    }
                    catch (Exception e)
                    {
                        Failed++;
                        Say("the drawing " + working.Mark +
                            " could not be saved: " + e.Message);
                    }
                }
            }

            string msg = "Wall framing annotations: " + Made + " created, " +
                         Failed + " failed, " + Skipped + " skipped, in " +
                         done + " drawing(s).";
            Say("");
            Say(msg);
            result.Ok = (Failed == 0 && done > 0);
            result.Made = Made;
            result.Failed = Failed;
            result.Message = msg;
            if (summary.Count > 1) result.Message += "\n" + string.Join("\n", summary.ToArray());
            result.Log = Log.ToString();
        }

        /// <summary>
        /// Dimensions ONE drawing: reads the model, derives the annotation
        /// rules and writes them into the drawing.
        /// </summary>
        /// <returns>True when the drawing was dimensioned.</returns>
        static bool DimensionDrawing(Drawing dr, TSM.Model model, WfResult result, int edition)
        {
            // ---- pick the plan view (looking straight down)
            View view = PlanView(dr);
            if (view == null)
            {
                Say("no plan view was found in this drawing - it is left alone");
                return false;
            }
            return DimensionView(view, dr, model, result, edition);
        }

        /// <summary>
        /// Dimensions ONE given wall-layout plan view as a run of its own (the
        /// counters, the log and the model read are reset first; the drawing
        /// is NOT saved).  For a macro that builds the view itself - P142_BASE_C_W.
        /// </summary>
        /// <param name="edition">1 for Pro, 0 for the free edition (use LicenceEdition()).</param>
        public static bool DimensionViewRun(View view, Drawing dr, WfResult result, int edition)
        {
            Log.Length = 0;
            Made = Failed = Skipped = 0;
            ProNoteSaid = false;
            Layouts = 0;
            ReadCoordinateSystem = null;
            DimHandler = new StraightDimensionSetHandler();
            bool ok = false;
            try { ok = DimensionView(view, dr, new TSM.Model(), result, edition); }
            catch (Exception e) { Failed++; Say("EXCEPTION " + e.GetType().Name + ": " + e.Message); }
            try { dr.CommitChanges("P142 wall plan dimensions"); } catch (Exception) { }
            result.Ok = ok && Failed == 0;
            result.Made = Made;
            result.Failed = Failed;
            result.Log = Log.ToString();
            return ok;
        }

        /// <summary>
        /// Dimensions ONE given wall-layout plan view (the body of the wall
        /// layout macro, without the search for the plan view).  Used by the
        /// wall layout macros through DimensionDrawing and by P142_BASE_C_W for
        /// the wall framing plan of a BASE, CEILING, WALL drawing.
        /// </summary>
        /// <returns>False when the view shows no wall panel.</returns>
        public static bool DimensionView(View view, Drawing dr, TSM.Model model, WfResult result, int edition)
        {
            ActiveView = view;
            ActiveDrawing = dr;

            Say("view '" + view.Name + "'  scale 1:" + view.Attributes.Scale);

            // ---- remove the output of a previous run of this macro
            CleanUp(view, dr);

            // ---- read the model
            ReadWallParts(view, model);
            Say("wall parts read: " + AllParts.Count);

            // ---- the wall panels this drawing shows
            SelectPanels(view);
            Say("wall panels: " + Panels.Count);
            if (Panels.Count == 0)
            {
                Say("no wall panel was found for this drawing - it is left alone");
                return false;
            }

            // ---- the bounding box of THIS wall layout
            //
            // The box is the box of the panels of this drawing, not of the
            // whole model: a model has one block per drawing, and the blocks
            // are far apart.
            Bx0 = double.MaxValue; By0 = double.MaxValue;
            Bx1 = double.MinValue; By1 = double.MinValue;
            foreach (WfPanel p in Panels)
            {
                Bx0 = Math.Min(Bx0, p.XLo); By0 = Math.Min(By0, p.YLo);
                Bx1 = Math.Max(Bx1, p.XHi); By1 = Math.Max(By1, p.YHi);
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
                DimChain(view, dr, "bays " + p.Name, chain, horiz, perp, up, Settings.PaperDistances.Panel);
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

            return true;
        }

        // ================================================================
        // THE AIO ENTRY POINT - a sheet with MANY wall framing layouts
        // ================================================================
        /// <summary>
        /// The core of the AIO macro, with a choice about saving.
        /// </summary>
        /// <param name="dr">The drawing that was active when the macro started.</param>
        /// <param name="result">Receives the outcome of the run.</param>
        /// <param name="save">True to save the drawing that was written to.</param>
        public static void ExecuteAio(Drawing dr, WfResult result, bool save)
        {
            Log.Length = 0;
            Made = Failed = Skipped = 0;
            ProNoteSaid = false;
            Layouts = 0;
            ReadCoordinateSystem = null;      // a new run reads the model again

            if (dr == null)
            {
                result.Ok = false;
                result.Message = "No active drawing.";
                result.Log = Log.ToString();
                return;
            }

            // The AIO macro works on the ACTIVE drawing and nowhere else:
            // it is a GA sheet with many wall framing layouts, and the
            // drawings of the single layouts are the work of the other
            // macro.
            DrawingHandler dh = new DrawingHandler();
            Drawing working = dh.GetActiveDrawing();
            if (working == null) working = dr;

            if (IsProtected(working))
            {
                Say("the drawing " + working.Mark +
                    " is issued or frozen - it is not written to");
                result.Ok = false;
                result.Message = "The drawing " + working.Mark +
                    " is issued or frozen - it is not written to.";
                result.Log = Log.ToString();
                return;
            }

            TSM.Model model = new TSM.Model();
            DimHandler = new StraightDimensionSetHandler();

            Say("=== drawing " + working.Mark + "  (" + working.Name + ") ===");

            bool done = DimensionLayouts(working, model, result);

            // CommitChanges() alone does not persist the objects (API
            // trap 1): the drawing has to be saved.
            if (save && done)
            {
                try
                {
                    dh.SaveActiveDrawing();
                }
                catch (Exception e)
                {
                    Failed++;
                    Say("the drawing " + working.Mark +
                        " could not be saved: " + e.Message);
                }
            }

            string msg = "Wall framing annotations: " + Made + " created, " +
                         Failed + " failed, " + Skipped + " skipped.";
            if (Layouts > 0)
            {
                msg += "  " + Layouts + " wall framing layout(s) on the sheet.";
            }
            Say("");
            Say(msg);
            result.Ok = (Failed == 0 && done);
            result.Made = Made;
            result.Failed = Failed;
            result.Message = msg;
            result.Log = Log.ToString();
        }

        /// <summary>
        /// Dimensions EVERY wall framing layout of a drawing, each one as if
        /// it were alone on its own sheet.
        ///
        /// The macro of the single-layout drawings treats a drawing as ONE
        /// wall framing layout.  A GA sheet carries the layouts of many
        /// blocks, and the chains of one block must not run into the next
        /// one: the bottom band chain and the top outline of the whole
        /// sheet would run through every other layout, and the drawing
        /// would show one long chain instead of one chain per framing.
        ///
        /// The panels are therefore grouped into the layouts they belong to
        /// (the block of a panel is the part of its assembly position
        /// before the first dash), and every layout is dimensioned on its
        /// own: its band chains, its top outline, its overall dimensions,
        /// its north run, its wall-panel marks, its section marks and its
        /// notes.
        /// </summary>
        /// <returns>True when the drawing was dimensioned.</returns>
        static bool DimensionLayouts(Drawing dr, TSM.Model model, WfResult result)
        {
            View view = PlanView(dr);
            if (view == null)
            {
                Say("no plan view was found in this drawing - it is left alone");
                return false;
            }
            return DimensionLayoutsOf(view, dr, model, result);
        }

        /// <summary>
        /// The AIO macro's work on ONE given plan view, as a run of its own:
        /// counters, log and model read reset first; the drawing is NOT saved.
        /// For a macro that builds the view itself - P142_BASE_C_W uses it for
        /// the wall framing plan of a BASE, CEILING, WALL drawing.
        /// </summary>
        public static bool DimensionViewAioRun(View view, Drawing dr, WfResult result)
        {
            Log.Length = 0;
            Made = Failed = Skipped = 0;
            ProNoteSaid = false;
            Layouts = 0;
            ReadCoordinateSystem = null;
            DimHandler = new StraightDimensionSetHandler();
            bool ok = false;
            SnapToWalls = true;
            try { ok = DimensionLayoutsOf(view, dr, new TSM.Model(), result); }
            catch (Exception e) { Failed++; Say("EXCEPTION " + e.GetType().Name + ": " + e.Message); }
            finally { SnapToWalls = false; }
            try { dr.CommitChanges("P142 wall plan annotations (AIO)"); } catch (Exception) { }
            result.Ok = ok && Failed == 0;
            result.Made = Made;
            result.Failed = Failed;
            if (string.IsNullOrEmpty(result.Message)) result.Message = "Wall framing annotations: " + Made + " created, " + Failed + " failed, " + Skipped + " skipped.";
            result.Log = Log.ToString();
            return ok;
        }

        /// <summary>The body of DimensionLayouts, for a given plan view.</summary>
        static bool DimensionLayoutsOf(View view, Drawing dr, TSM.Model model, WfResult result)
        {
            ActiveView = view;
            ActiveDrawing = dr;

            Say("view '" + view.Name + "'  scale 1:" + view.Attributes.Scale);

            // ---- remove the output of a previous run of this macro
            //
            // The section marks stay:  they belong to the layouts of the
            // sheet, and the ones of the client carry the tag of the macro
            // as well.  Every layout that has no section mark of its own
            // gets the ones of the drawing of its block.
            CleanUp(view, dr, true);

            // ---- read the model
            ReadWallParts(view, model);
            Say("wall parts read: " + AllParts.Count);

            // ---- the wall panels this drawing shows
            SelectPanels(view);
            Say("wall panels: " + Panels.Count);
            if (Panels.Count == 0)
            {
                Say("no wall panel was found for this drawing - it is left alone");
                return false;
            }

            // ---- the wall framing layouts of the sheet
            List<WfLayout> layouts = WfRules.LayoutGroups(Panels);
            Layouts = layouts.Count;
            Say("wall framing layouts: " + layouts.Count);

            // ---- the stud-spacing chain of every wall panel
            //
            // The chain belongs to the PANEL, not to the layout, so it is
            // drawn once for every panel of the sheet.
            foreach (WfPanel p in Panels)
            {
                List<double> chain = WfRules.ChainStations(p, TOL);
                if (chain.Count < 2) continue;

                bool horiz = p.Horiz;
                double perp = horiz ? p.Bottom.Y0 : p.Bottom.X0;
                Vector up = horiz ? new Vector(0, -1, 0) : new Vector(-1, 0, 0);
                DimChain(view, dr, "bays " + p.Name, chain, horiz, perp, up, Settings.PaperDistances.Panel);
            }

            // ---- every layout, on its own
            foreach (WfLayout lay in layouts)
            {
                Say("");
                Say("=== layout " + lay.Block + "  (" + lay.Panels.Count +
                    " panels, X " + lay.X0.ToString("F0") + " .. " + lay.X1.ToString("F0") + ") ===");

                // the box and the panels of THIS layout: the Pro module
                // reads them from here
                Panels.Clear();
                Panels.AddRange(lay.Panels);
                Bx0 = lay.X0; By0 = lay.Y0; Bx1 = lay.X1; By1 = lay.Y1;

                // the band chain of every edge of the layout
                DimBand(view, dr, "band bottom", WfBandEdge.Bottom, Bx0, By0, Bx1, By1);
                DimBand(view, dr, "band left", WfBandEdge.Left, Bx0, By0, Bx1, By1);
                DimBand(view, dr, "band right", WfBandEdge.Right, Bx0, By0, Bx1, By1);

                // the Pro stages of this layout: the top outline, the two
                // overall dimensions, the north run, the wall-panel marks,
                // the section marks and the text notes
                RunProLayout(result, lay.Block);
            }

            return true;
        }

    }
}
