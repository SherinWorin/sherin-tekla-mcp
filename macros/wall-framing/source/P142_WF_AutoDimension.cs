#pragma warning disable 1633
#pragma reference "Tekla.Macros.Runtime"
#pragma reference "Tekla.Structures"
#pragma reference "Tekla.Structures.Drawing"
#pragma reference "Tekla.Structures.Model"
#pragma warning restore 1633

// P142_WF_AutoDimension.cs  -  Tekla Structures 2025 drawing macro
//
// Reproduces the annotations of the WALL FRAMING LAYOUT GA drawing
// (drawing mark [50]) into another GA drawing of the same model (e.g. [57]):
//
//   * StraightDimensionSets - the stud-spacing chain of every wall panel,
//                             the band chains of the bottom / left / right
//                             edge of the layout, the top outline of the
//                             layout, its overall height and width, and
//                             the north run
//   * wall-panel marks      - BP4-WP1 .. BP4-WP11, associative ASSEMBLY_POS
//   * section marks         - S1 .. S5 (copied from the reference drawing)
//   * text notes            - LGS / gypsum board typical notes
//
// The annotation RULES live in WF_Rules.cs, a file with no Tekla reference,
// so they can be unit-tested without a running Tekla Structures
// (debug/test_rules.py, debug/test_marks.py).  This file only reads the
// model and writes drawing objects.  The rules were derived from the real
// GA drawing [50] of model P142-COREL REEF_BP4_MOCKUP-R00; see
// docs/WALL_FRAMING_RULES.md.
//
// RISK: MEDIUM.  It only creates drawing objects (dimensions / marks / texts)
// inside the active drawing.  It never touches the model.
// Test on a COPY of the target drawing first.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;
using Tekla.Structures;
using Tekla.Structures.Drawing;
using Tekla.Structures.Geometry3d;
using TSM = Tekla.Structures.Model;
using WF = P142.WallFraming;
// System.Windows.Forms also defines a "View" and a "Text"; alias the Tekla ones.
using View = Tekla.Structures.Drawing.View;
using DrawingText = Tekla.Structures.Drawing.Text;

namespace UserMacros
{
    public sealed class Macro
    {
        // ------------------------------------------------------------- config
        /// <summary>Geometric tolerance in millimetres.</summary>
        const double TOL = 0.5;

        /// <summary>User property written on every object the macro creates.</summary>
        const string TAG_PROP = "P142_WF_AUTODIM";

        /// <summary>Value of the user property, so a re-run can find its own output.</summary>
        const string TAG_VALUE = "1";

        /// <summary>Assembly-position prefix of the wall panels.</summary>
        const string PANEL_PREFIX = "BP4-WP";

        /// <summary>Distance of a wall-panel label from its leader line, in mm.</summary>
        const double MARK_OFFSET = 29.25;

        /// <summary>Paper distance of a panel chain, in mm.</summary>
        const double PAPER_PANEL = 8.8;

        /// <summary>Paper distance of a band chain, in mm.</summary>
        const double PAPER_BAND = 15.9;

        /// <summary>
        /// Paper distance of the top outline, in mm (21.82 in the source
        /// drawing [50], where the outline is 370.92 mm above the geometry
        /// at a scale of 1:17).
        /// </summary>
        const double PAPER_OUTLINE = 21.82;

        /// <summary>
        /// The widest gap in the wall layout that the top outline bridges
        /// without measuring it, in mm (2.0 in the source drawing [50]).
        /// </summary>
        const double NOTCH = 2.0;

        /// <summary>
        /// Paper distance of the overall height dimension, in mm (24.27 in
        /// the source drawing [50]).
        /// </summary>
        const double PAPER_OVERALL_H = 24.27;

        /// <summary>
        /// Paper distance of the overall width dimension, in mm (27.74 in
        /// the source drawing [50]).
        /// </summary>
        const double PAPER_OVERALL_W = 27.74;

        /// <summary>
        /// Paper distance of the north-run dimension line from the wall it
        /// measures, in mm.  The source drawing [50] puts the north run and
        /// the top outline on top of each other (both are measured northwards,
        /// 21.63 and 21.82 mm from their geometry); here the north run is
        /// moved further north so that both chains stay readable.
        /// </summary>
        const double PAPER_NORTH = 30.0;

        /// <summary>
        /// The title fragments that identify a WALL LAYOUT drawing.  The
        /// macro works on any GA drawing of the model whose title holds one
        /// of them - the mark number does not matter, so the same macro
        /// works on every computer of the project.
        /// </summary>
        static readonly string[] WALL_LAYOUT_TITLES = {
            "WALL FRAMING", "WALL LAYOUT", "WALL FRAMING LAYOUT"
        };

        /// <summary>
        /// The mark of the hand-made reference drawing, when the model has
        /// one.  It is only a preference: when this drawing does not exist
        /// the reference is found by its title instead.
        /// </summary>
        const string REFERENCE_DRAWING = "[50]";

        /// <summary>Style of the dimensions in the source drawing [50].</summary>
        static readonly string[] DIM_STYLE = {
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

        /// <summary>Style of the text notes in the source drawing [50].</summary>
        static readonly string[] NOTE_STYLE = {
            "Font.Color=Black","Font.Name=Arial","Font.Height=2.5","Font.Italic=False","Font.Bold=False",
            "Angle=0" };

        // ------------------------------------------------------------- state
        static readonly StringBuilder Log = new StringBuilder();
        static int Made, Failed, Skipped;

        /// <summary>All wall parts read from the model.</summary>
        static readonly List<WF.WfPart> AllParts = new List<WF.WfPart>();

        /// <summary>The wall panels, grouped from the parts.</summary>
        static readonly List<WF.WfPanel> Panels = new List<WF.WfPanel>();

        // ------------------------------------------------------------- helpers
        static void Say(string s)
        {
            Log.AppendLine(s);
        }

        static PropertyInfo FindProp(Type t, string name)
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
        static void SetPath(object target, string path, string value)
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

        // ------------------------------------------------------------- model read
        /// <summary>
        /// Reads the plan-view extents of every wall part of the model, in the
        /// coordinate system of the given view.
        /// </summary>
        static void ReadWallParts(View view, TSM.Model model)
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
                        if (!WF.WfRules.IsWallPart(p.Name)) continue;
                        TSM.Solid so = p.GetSolid();
                        if (so == null) continue;
                        string apos = "";
                        p.GetReportProperty("ASSEMBLY_POS", ref apos);

                        WF.WfPart wp = new WF.WfPart();
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
        static StraightDimensionSetHandler DimHandler;

        /// <summary>
        /// Creates a dimension set.  The points must be given in the order
        /// they appear along the dimension line.
        /// </summary>
        static void Dim(View view, Drawing dr, string what, List<Point> pts, Vector up, double paperDist)
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
        static void DimChain(View view, Drawing dr, string what, List<double> stations,
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
        /// Creates a wall-panel mark whose content is an associative
        /// ASSEMBLY_POS property element.
        ///
        /// The Mark must be attached to a drawing ModelObject; the only way to
        /// obtain one for an arbitrary model part is to read the drawing Part
        /// that already represents it (Tekla.Structures.Drawing.ModelObject is
        /// abstract and its ModelIdentifier setter is internal).
        ///
        /// The TEXT ANGLE is set through MarkBaseAttributes.RotationAngle, the
        /// only public lever: MarkBaseAttributes.Angle ("the actual angle of
        /// the mark") has an INTERNAL setter, so C# cannot assign it, and
        /// writing it through reflection is pointless - the drawing engine
        /// recomputes Angle from the leader when the drawing is saved.
        /// Setting RotationAngle makes the engine compute the matching Angle
        /// (verified: 90 -> Angle 90, 0 -> Angle 0).
        /// </summary>
        static void PanelMark(View view, Drawing dr, WF.WfPanel pan)
        {
            try
            {
                // find the drawing Part of this panel's bottom track
                ModelObject target = FindDrawingPart(view, pan.Bottom.Id);
                if (target == null)
                {
                    Failed++;
                    Say("FAIL mark " + pan.Name + " : no drawing part for model id " + pan.Bottom.Id);
                    return;
                }

                WF.WfMarkPlacement mp = WF.WfRules.MarkPlacement(pan, MARK_OFFSET);

                Point ins = new Point(mp.InsX, mp.InsY, 0);
                Point start = new Point(mp.StartX, mp.StartY, 0);
                Point end = new Point(mp.EndX, mp.EndY, 0);

                Mark mk = new Mark(target);
                mk.InsertionPoint = ins;
                mk.Placing = new AlongLinePlacing(start, end);

                PropertyElement pe = new PropertyElement(
                    PropertyElement.PropertyElementType.PartMarkPropertyElementTypes.AssemblyPosition());
                pe.Name = "ASSEMBLY_POS";
                // Value is filled in by Tekla from the model's ASSEMBLY_POS
                // (its setter is internal); Name + PropertyType identify it.
                mk.Attributes.Content.Add(pe);

                // the text angle, set BEFORE the mark is inserted
                SetRotationAngle(mk, mp.Angle);

                mk.Insert();
                dr.CommitChanges();
                mk.SetUserProperty(TAG_PROP, TAG_VALUE);
                Made++;
                Say("OK   mark " + pan.Name + " angle " + mp.Angle + " at (" +
                    ins.X.ToString("F2") + "," + ins.Y.ToString("F2") + ")");
            }
            catch (Exception e)
            {
                Failed++;
                Say("FAIL mark " + pan.Name + " : " + e.Message);
            }
        }

        /// <summary>
        /// Sets the text angle of a mark through MarkBaseAttributes.RotationAngle.
        ///
        /// The angle is written to a copy of the attributes and the copy is
        /// written back, because the getter of Mark.Attributes returns a COPY -
        /// changing it in place has no effect.
        /// </summary>
        static void SetRotationAngle(Mark mk, int degrees)
        {
            Mark.MarkAttributes attrs = mk.Attributes;
            attrs.RotationAngle = (double)degrees;
            mk.Attributes = attrs;
        }

        /// <summary>
        /// Returns the drawing object that represents the given model part,
        /// or null when the part is not shown in this view.
        /// </summary>
        static ModelObject FindDrawingPart(View view, int modelId)
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

        /// <summary>Creates a section mark.</summary>
        static void Section(View view, Drawing dr, string name, Point left, Point right)
        {
            try
            {
                SectionMark sm = new SectionMark(view, left, right);
                sm.Attributes.MarkName = name;
                sm.Attributes.LineWidth = 5.0;
                sm.Attributes.LineColor = DrawingColors.Cyan;
                sm.Insert();
                dr.CommitChanges();
                sm.SetUserProperty(TAG_PROP, TAG_VALUE);
                Made++;
                Say("OK   section " + name);
            }
            catch (Exception e)
            {
                Failed++;
                Say("FAIL section " + name + " : " + e.Message);
            }
        }

        /// <summary>
        /// Copies the section marks of a reference drawing into the target.
        ///
        /// The five section lines of the reference drawing [50] are hand
        /// placed: they do not lie on any model feature, they run well past
        /// the wall layout, and their positions carry no information that the
        /// model contains.  They are therefore copied verbatim instead of
        /// being re-derived, which reproduces them exactly.
        ///
        /// A non-active drawing can be read (Drawing.GetSheet() and
        /// View.GetAllObjects() work on a drawing that is not open in the
        /// editor), so the reference does not have to be activated.
        /// </summary>
        static int CopySections(View view, Drawing dr, Drawing reference)
        {
            int copied = 0;
            try
            {
                if (reference == null)
                {
                    Say("WARN section marks: no reference drawing found, skipped");
                    return 0;
                }
                string refMark = reference.Mark;

                List<SectionMark> src = new List<SectionMark>();
                DrawingObjectEnumerator ve = reference.GetSheet().GetViews();
                while (ve.MoveNext())
                {
                    View rv = ve.Current as View;
                    if (rv == null) continue;
                    DrawingObjectEnumerator en = rv.GetAllObjects(typeof(SectionMark));
                    while (en.MoveNext())
                    {
                        SectionMark s = en.Current as SectionMark;
                        if (s != null) src.Add(s);
                    }
                    break;      // one plan view is enough
                }
                if (src.Count == 0)
                {
                    Say("WARN section marks: reference drawing " + refMark +
                        " has none");
                    return 0;
                }

                // copy them in the order of the reference
                src.Sort(delegate (SectionMark a, SectionMark b)
                {
                    return string.Compare(a.Attributes.MarkName, b.Attributes.MarkName,
                                          StringComparison.Ordinal);
                });
                foreach (SectionMark s in src)
                {
                    Section(view, dr, s.Attributes.MarkName, s.LeftPoint, s.RightPoint);
                    copied++;
                }
                Say("section marks copied from " + refMark + ": " + copied);
            }
            catch (Exception e)
            {
                Failed++;
                Say("FAIL section copy : " + e.Message);
            }
            return copied;
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
        static int CleanUp(View view, Drawing dr)
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
        static int RemoveTagged<T>(View view, Drawing dr) where T : DrawingObject
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
        static bool IsTagged(DrawingObject obj)
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
        static bool Delete(DrawingObject obj)
        {
            if (obj is StraightDimensionSet) return ((StraightDimensionSet)obj).Delete();
            if (obj is SectionMark) return ((SectionMark)obj).Delete();
            if (obj is DrawingText) return ((DrawingText)obj).Delete();
            if (obj is Mark) return ((Mark)obj).Delete();
            return false;
        }

        /// <summary>Creates a text note.</summary>
        static void Note(View view, Drawing dr, string text, Point ins)
        {
            try
            {
                DrawingText t = new DrawingText(view, ins, text);
                t.Attributes.Font.Color = DrawingColors.Black;
                t.Attributes.Font.Height = 2.5;
                t.Attributes.Font.Name = "Arial";
                t.Attributes.Frame = new Frame(FrameTypes.Line, DrawingColors.Black);
                t.Attributes.Angle = 0.0;
                t.Insert();
                dr.CommitChanges();
                t.SetUserProperty(TAG_PROP, TAG_VALUE);
                Made++;
                Say("OK   text '" + text.Replace("\n", "/") + "'");
            }
            catch (Exception e)
            {
                Failed++;
                Say("FAIL text : " + e.Message);
            }
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
        static bool IsWallLayout(Drawing d)
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
        static bool IsProtected(Drawing d)
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
        static Drawing ResolveTargetDrawing(Drawing active, out string note)
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

        /// <summary>
        /// Finds the drawing the hand-made section marks are copied from.
        ///
        /// The preferred mark is used when the model has it.  Otherwise
        /// another wall layout drawing of the model is used, provided it
        /// actually carries section marks - the target drawing itself is
        /// never used as its own reference.
        /// </summary>
        /// <param name="target">The drawing that is being dimensioned.</param>
        static Drawing FindReferenceDrawing(Drawing target)
        {
            List<Drawing> candidates = new List<Drawing>();
            Drawing preferred = null;
            try
            {
                DrawingHandler dh = new DrawingHandler();
                DrawingEnumerator de = dh.GetDrawings();
                while (de.MoveNext())
                {
                    Drawing d = de.Current;
                    if (d.Mark.Replace(" ", "") == REFERENCE_DRAWING.Replace(" ", ""))
                    {
                        preferred = d;
                        continue;
                    }
                    if (target != null && d.Mark == target.Mark) continue;
                    if (IsWallLayout(d)) candidates.Add(d);
                }
            }
            catch { }

            if (preferred != null) return preferred;
            foreach (Drawing d in candidates)
                if (CountSectionMarks(d) > 0) return d;
            return null;
        }

        /// <summary>The number of section marks of a drawing.</summary>
        static int CountSectionMarks(Drawing d)
        {
            int n = 0;
            try
            {
                DrawingObjectEnumerator ve = d.GetSheet().GetViews();
                while (ve.MoveNext())
                {
                    View rv = ve.Current as View;
                    if (rv == null) continue;
                    DrawingObjectEnumerator en = rv.GetAllObjects(typeof(SectionMark));
                    while (en.MoveNext()) if (en.Current is SectionMark) n++;
                    break;      // one plan view is enough
                }
            }
            catch { }
            return n;
        }

        // ------------------------------------------------------------- main
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

        /// <summary>
        /// The macro's core: reads the model, derives the annotation rules and
        /// writes them into the given drawing.  It touches no user interface,
        /// so it can be called from a test.
        /// </summary>
        /// <param name="dr">The target drawing (must be the active drawing).</param>
        /// <param name="result">Receives the outcome of the run.</param>
        public static void Execute(Drawing dr, Result result)
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
            Panels.AddRange(WF.WfRules.BuildPanels(AllParts, PANEL_PREFIX, TOL));
            if (Panels.Count == 0)
            {
                Panels.AddRange(WF.WfRules.BuildPanelsAuto(AllParts));
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
            double bx0 = double.MaxValue, by0 = double.MaxValue;
            double bx1 = double.MinValue, by1 = double.MinValue;
            foreach (WF.WfPart p in AllParts)
            {
                bx0 = Math.Min(bx0, p.X0); by0 = Math.Min(by0, p.Y0);
                bx1 = Math.Max(bx1, p.X1); by1 = Math.Max(by1, p.Y1);
            }
            if (bx1 < bx0) { bx0 = 0; by0 = 0; bx1 = 1000; by1 = 1000; }

            // ================================================================
            // 1. DIMENSION SETS
            // ================================================================
            // 1a. the stud-spacing chain of every wall panel
            foreach (WF.WfPanel p in Panels)
            {
                List<double> chain = WF.WfRules.ChainStations(p, TOL);
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
            DimBand(view, dr, "band bottom", WF.WfBandEdge.Bottom, bx0, by0, bx1, by1);
            DimBand(view, dr, "band left", WF.WfBandEdge.Left, bx0, by0, bx1, by1);
            DimBand(view, dr, "band right", WF.WfBandEdge.Right, bx0, by0, bx1, by1);

            // 1c. the top outline of the layout (one dimension set on the
            //     polyline of the upper boundary)
            List<double> outline = WF.WfRules.TopOutlinePolyline(Panels, NOTCH, TOL);
            DimOutline(view, dr, outline);

            // 1d. the two overall dimensions of the layout
            DimOverall(view, dr, bx0, by0, bx1, by1, outline);

            // 1e. the north run: the run of the northern wall, from its start
            //     to the end of the layout
            DimNorthRun(view, dr);

            // ================================================================
            // 2. WALL PANEL MARKS
            // ================================================================
            foreach (WF.WfPanel p in Panels)
            {
                PanelMark(view, dr, p);
            }

            // ================================================================
            // 3. SECTION MARKS  (S1..S5)
            // ================================================================
            // The section lines of the reference drawing are hand placed and
            // carry no information the model holds, so they are copied from
            // the reference drawing instead of being re-derived.
            double x0 = double.MaxValue, y0 = double.MaxValue;
            double x1 = double.MinValue, y1 = double.MinValue;
            foreach (WF.WfPart p in AllParts)
            {
                x0 = Math.Min(x0, p.X0); y0 = Math.Min(y0, p.Y0);
                x1 = Math.Max(x1, p.X1); y1 = Math.Max(y1, p.Y1);
            }
            if (x1 < x0) { x0 = 0; y0 = 0; x1 = 1000; y1 = 1000; }

            CopySections(view, dr, FindReferenceDrawing(dr));

            // ================================================================
            // 4. TEXT NOTES
            // ================================================================
            Note(view, dr, "LGS\nC61*35*0.7 TYP.", new Point((x0 + x1) / 2.0 - 11.0, (y0 + y1) / 2.0 - 295.0, 0));
            Note(view, dr, "12.5mm  GYPSUM BOARD TYP.", new Point((x0 + x1) / 2.0 + 47.0, (y0 + y1) / 2.0 - 539.0, 0));
            Note(view, dr, "LGS\nC63*40*0.7 TYP.", new Point((x0 + x1) / 2.0 - 12.0, (y0 + y1) / 2.0 - 428.0, 0));

            string msg = "Wall framing annotations: " + Made + " created, " +
                         Failed + " failed, " + Skipped + " skipped.";
            Say(msg);
            result.Ok = (Failed == 0);
            result.Made = Made;
            result.Failed = Failed;
            result.Message = msg;
            result.Log = Log.ToString();
        }

        /// <summary>Creates the band chain of one edge of the layout.</summary>
        static void DimBand(View view, Drawing dr, string what, WF.WfBandEdge edge,
                            double bx0, double by0, double bx1, double by1)
        {
            List<double> vals = WF.WfRules.BandStations(Panels, edge, bx0, by0, bx1, by1, TOL);
            if (vals.Count < 2) return;

            bool horiz = (edge == WF.WfBandEdge.Bottom || edge == WF.WfBandEdge.Top);
            // the dimension line runs along the edge of the layout
            double line;
            switch (edge)
            {
                case WF.WfBandEdge.Bottom: line = by0; break;
                case WF.WfBandEdge.Top: line = by1; break;
                case WF.WfBandEdge.Left: line = bx0; break;
                default: line = bx1; break;
            }
            // the up vector points away from the layout
            Vector up = horiz ? new Vector(0, -1, 0) : new Vector(-1, 0, 0);
            DimChain(view, dr, what, vals, horiz, line, up, PAPER_BAND);
        }

        /// <summary>
        /// Creates the top outline of the layout: ONE dimension set on the
        /// polyline of the upper boundary of the walls.
        ///
        /// The polyline is the one of the source drawing [50]: it runs along
        /// the north face of the walls that run along X and is chamfered
        /// across the walls that run along Y.  The dimension line is placed
        /// north of the outline, outside the layout.
        /// </summary>
        static void DimOutline(View view, Drawing dr, List<double> poly)
        {
            if (poly.Count < 4) return;

            List<Point> pl = new List<Point>();
            for (int i = 0; i + 1 < poly.Count; i += 2)
                pl.Add(new Point(poly[i], poly[i + 1], 0));
            Dim(view, dr, "outline", pl, new Vector(0, 1, 0), PAPER_OUTLINE);
        }

        /// <summary>
        /// Creates the two OVERALL dimensions of the layout: its height and
        /// its width.
        ///
        /// The height is measured on the west edge of the layout, from the
        /// south-west corner to the north-west corner.  The width is
        /// measured between the two ends of the top outline, which is the
        /// diagonal of the source drawing [50].
        /// </summary>
        static void DimOverall(View view, Drawing dr,
                               double bx0, double by0, double bx1, double by1,
                               List<double> outline)
        {
            WF.WfOverall o = WF.WfRules.Overall(bx0, by0, bx1, by1, outline);

            // the height of the layout, on its west edge
            List<Point> h = new List<Point>();
            h.Add(new Point(o.Hx0, o.Hy0, 0));
            h.Add(new Point(o.Hx1, o.Hy1, 0));
            Dim(view, dr, "overall height", h, new Vector(-1, 0, 0), PAPER_OVERALL_H);

            // the width of the layout, between the ends of the top outline
            if (outline.Count < 4) return;
            List<Point> w = new List<Point>();
            w.Add(new Point(o.Wx0, o.Wy0, 0));
            w.Add(new Point(o.Wx1, o.Wy1, 0));
            Dim(view, dr, "overall width", w, new Vector(0, 1, 0), PAPER_OVERALL_W);
        }

        /// <summary>
        /// Creates the NORTH RUN of the layout: the run of the northern wall,
        /// from the start of that wall to the end of the layout, with the end
        /// of the wall itself as the middle station.
        ///
        /// The source drawing [50] measures 1799.32 - 3110.82 - 3187.32 on
        /// the north wall (BP4-WP5).
        /// </summary>
        static void DimNorthRun(View view, Drawing dr)
        {
            WF.WfNorthRun run = WF.WfRules.NorthRun(Panels, NOTCH, TOL);
            if (run == null || run.Stations.Count < 2) return;
            DimChain(view, dr, "north run", run.Stations, true, run.Level,
                     new Vector(0, 1, 0), PAPER_NORTH);
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
