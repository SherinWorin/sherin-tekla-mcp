// self-check: the colour code splits LGS / wall gypsum / HRS / ceiling gypsum as the BCW plans need
using System; using P142.WallFraming;
class C { static int bad;
  static void T(string name, string prof, string want) { var r = WfIsoColours.Match(name, prof); string got = r == null ? "none" : r.Label; bool ok = got == want; if (!ok) bad++; Console.WriteLine((ok ? "OK   " : "FAIL ") + name + " " + prof + " -> " + got); }
  static int Main() {
    T("STUD", "C61*35*0.7", "LGS_GREY"); T("STUD", "CC61-0.7-7-35", "LGS_GREY"); T("BOTTOM_TRACK", "C63*40*0.7", "LGS_GREY");
    T("GYPSUM_BOARD", "PL12.5*1200", "WALL_ORANGE"); T("CEILING_GYPSUM", "PL12.5*1270", "CEILING_VIOLET");
    T("BEAM", "RHS120*60*4.0", "HRS_GREEN"); T("CEILING BEAM", "RHS40*20*3", "HRS_GREEN"); T("BEAM", "RHS60*30*4", "HRS_GREEN");
    Console.WriteLine(bad == 0 ? "ALL PASSED" : bad + " FAILED"); return bad; } }
