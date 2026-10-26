// self-check of WfCore.SnapPoints: L-shaped pod, left chain at the bounding box line x=0;
// the upper wall starts at x=1250, the lower wall at x=0 -> points snap to 1250 / 0.
using System; using System.Collections.Generic; using P142.WallFraming; using Tekla.Structures.Geometry3d;
class C { static int Main() {
  WfCore.AllParts.Clear();
  WfCore.AllParts.Add(new WfPart { Name = "BOTTOM_TRACK", X0 = 1250, Y0 = 1500, X1 = 1313, Y1 = 2875 });   // upper wall
  WfCore.AllParts.Add(new WfPart { Name = "BOTTOM_TRACK", X0 = 0, Y0 = 0, X1 = 63, Y1 = 1500 });          // lower wall
  var r = WfCore.SnapPoints(new List<Point> { new Point(0, 2875, 0), new Point(0, 2000, 0), new Point(0, 500, 0), new Point(0, 3500, 0) }, new Vector(-1, 0, 0));
  bool ok = r[0].X == 1250 && r[1].X == 1250 && r[2].X == 0 && r[3].X == 0;   // no wall at y=3500 -> unchanged
  // bottom chain (up -y): a point already on the wall face stays put
  var b = WfCore.SnapPoints(new List<Point> { new Point(30, 0, 0) }, new Vector(0, -1, 0));
  ok &= b[0].Y == 0;
  foreach (var p in r) Console.WriteLine(p.X + "," + p.Y);
  Console.WriteLine(ok ? "SNAP CHECK PASSED" : "SNAP CHECK FAILED"); return ok ? 0 : 1; } }
