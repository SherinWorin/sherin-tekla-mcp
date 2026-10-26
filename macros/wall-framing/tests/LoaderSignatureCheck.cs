// Self-check: every name the loaders look up by reflection exists with the right signature,
// and every member WF_Pro.dll uses resolves against the new WF_Core / WF_Rules.
using System; using System.IO; using System.Reflection; using System.Linq;
class Check {
  static int bad = 0; static string dir;
  static void Ok(bool c, string what) { Console.WriteLine((c ? "OK   " : "FAIL ") + what); if (!c) bad++; }
  static Assembly A(string n) { return Assembly.LoadFrom(Path.Combine(dir, n + ".dll")); }
  static Type Drawing;
  static void Result(Type r, params string[] f) { foreach (var n in f) Ok(r != null && r.GetField(n) != null, "  " + (r==null?"?":r.Name) + "." + n); }
  static void Static(Type t, string m, params Type[] p) { Ok(t != null && t.GetMethod(m, BindingFlags.Public|BindingFlags.Static, null, p, null) != null, "  " + (t==null?"?":t.Name) + "." + m + "(" + string.Join(", ", p.Select(x => x.Name)) + ")"); }
  static void Main(string[] a) {
    dir = a[0];
    AppDomain.CurrentDomain.AssemblyResolve += (s, e) => { var f = Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll"); return File.Exists(f) ? Assembly.LoadFrom(f) : null; };
    Drawing = A("Tekla.Structures.Drawing").GetType("Tekla.Structures.Drawing.Drawing");
    Console.WriteLine("P142_BASE_C_W -> WF_BaseCW.dll");
    var bc = A("WF_BaseCW"); var bcc = bc.GetType("P142.WallFraming.WfBaseCwCore"); var bcr = bc.GetType("P142.WallFraming.WfBaseCwResult");
    Static(bcc, "Execute", Drawing, bcr, typeof(bool)); Result(bcr, "Ok", "Failed", "Message", "Log");
    Console.WriteLine("P142_WF_Isometric -> WF_Isometric.dll");
    var iso = A("WF_Isometric"); var ic = iso.GetType("P142.WallFraming.WfIsometricCore"); var ir = iso.GetType("P142.WallFraming.WfIsoResult");
    Static(ic, "Execute", Drawing, ir, typeof(bool)); Static(ic, "IsIsometricViewDrawing", Drawing); Result(ir, "Ok", "Message", "Log", "Made", "Failed");
    Console.WriteLine("P142_WF_Section -> WF_Section.dll");
    var sec = A("WF_Section"); var sc = sec.GetType("P142.WallFraming.WfSectionCore"); var sr = sec.GetType("P142.WallFraming.WfSectionResult");
    Static(sc, "Execute", Drawing, sr, typeof(bool)); Static(sc, "IsProtected", Drawing); Result(sr, "Ok", "Message", "Log", "Made", "Failed");
    Console.WriteLine("P142_WF_AutoDimension + AIO -> WF_Core.dll");
    var core = A("WF_Core"); var cc = core.GetType("P142.WallFraming.WfCore"); var cr = core.GetType("P142.WallFraming.WfResult");
    Static(cc, "LicenceEdition"); Ok(cc.GetField("LicenceMessage") != null, "  WfCore.LicenceMessage");
    Static(cc, "Execute", Drawing, cr, typeof(int)); Static(cc, "Execute", Drawing, cr, typeof(int), typeof(bool));
    Ok(cc.GetMethods().Any(m => m.Name == "ExecuteAio" && m.GetParameters().Length == 3), "  WfCore.ExecuteAio(3 params)");
    Result(cr, "Ok", "Message", "Made", "Failed", "Log");
    Console.WriteLine("WF_BaseCW -> WF_Core (wall plan)");
    var view = A("Tekla.Structures.Drawing").GetType("Tekla.Structures.Drawing.View");
    Static(cc, "DimensionViewAioRun", view, Drawing, cr);
    Console.WriteLine("WF_Core -> WF_Pro.dll (Pro stages)");
    var pro = A("WF_Pro"); var pt = pro.GetType("P142.WallFraming.WfPro");
    Ok(pt != null && pt.GetMethod("Execute") != null && pt.GetMethod("ExecuteLayout") != null, "  WfPro.Execute / ExecuteLayout");
    var rv = (int)pt.GetField("RulesApiVersion").GetRawConstantValue(); var iv = (int)A("WF_Rules").GetType("P142.WallFraming.WfRules").GetField("ApiVersion").GetRawConstantValue();
    Ok(rv == iv, "  WF_Pro built for rules API " + rv + ", WF_Rules is " + iv);
    foreach (var asm in new[] { pro, A("WF_Rules_Pro") }) {
      int n = 0, miss = 0;
      for (int i = 1; i < 2000; i++) {
        try { asm.ManifestModule.ResolveMember(0x0A000000 | i); n++; }
        catch (ArgumentOutOfRangeException) { break; }
        catch (Exception e) { miss++; Console.WriteLine("     missing: " + e.Message); }
      }
      Ok(miss == 0, "  " + asm.GetName().Name + ": " + n + " member references resolve");
    }
    Console.WriteLine(bad == 0 ? "ALL CHECKS PASSED" : bad + " CHECK(S) FAILED");
    Environment.Exit(bad);
  }
}
