using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
namespace Kalidis.AIEngine;
// Explicit straight-wall L joints, local Sandbox gate and transaction handled by caller.
internal static class WallJointOperations {
 static double Mm(double n)=>UnitUtils.ConvertToInternalUnits(n,UnitTypeId.Millimeters);
 static double Cubic(double n)=>UnitUtils.ConvertFromInternalUnits(n,UnitTypeId.CubicMeters);
 static Line Axis(Wall w)=>w.Location is LocationCurve c&&c.Curve is Line l?l:throw new ArgumentException("Straight wall required");
 static IEnumerable<Solid> Solids(GeometryElement? g,int depth=0){
  if(depth>8)throw new InvalidOperationException("Geometry depth limit");
  if(g==null)yield break;
  foreach(var o in g){if(o is Solid s&&s.Faces.Size>0&&s.Volume>1e-9)yield return s;else if(o is GeometryInstance gi)foreach(var q in Solids(gi.GetInstanceGeometry(),depth+1))yield return q;}
 }
 static double Overlap(Wall a,Wall b){
  var x=Solids(a.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).Take(101).ToArray();
  var y=Solids(b.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})).Take(101).ToArray();
  if(x.Length==0||y.Length==0||x.Length>100||y.Length>100||x.Length*y.Length>400)throw new InvalidOperationException("Physical solid coverage unavailable");
  double v=0;foreach(var s in x)foreach(var q in y){using var i=BooleanOperationsUtils.ExecuteBooleanOperation(s,q,BooleanOperationsType.Intersect);v+=Math.Abs(i.Volume);}
  return Cubic(v);
 }
 internal static (object result,Action verify) Run(Document d,JsonElement r){
  Wall Get(string key,string uid)=>d.GetElement(new ElementId(r.GetProperty(key).GetInt64())) is Wall w&&w.UniqueId==r.GetProperty(uid).GetString()?w:throw new ArgumentException("Wall identity mismatch");
  var a=Get("firstId","firstUniqueId");var b=Get("secondId","secondUniqueId");
  int i=r.GetProperty("firstEnd").GetInt32(),j=r.GetProperty("secondEnd").GetInt32();
  if(a.Id==b.Id||a.LevelId!=b.LevelId||a.GetTypeId()!=b.GetTypeId()||i is <0 or >1||j is <0 or >1)throw new ArgumentException("Distinct same-type, same-level walls and valid endpoints required");
  var ids=new HashSet<ElementId>{a.Id,b.Id};
  void Guard(Wall w){
   if(w.Pinned||w.GroupId!=ElementId.InvalidElementId||w.WallType.Kind!=WallKind.Basic||w.StructuralUsage!=StructuralWallUsage.NonBearing||w.FindInserts(true,true,true,true).Count!=0||
      w.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED)?.AsInteger()!=0||w.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger()!=0||
      w.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE)?.AsElementId()!=ElementId.InvalidElementId||
      w.GetDependentElements(null).Select(d.GetElement).Any(e=>e is Dimension||e is IndependentTag||e is FamilyInstance||e is Opening||e is Sketch)||
      JoinGeometryUtils.GetJoinedElements(d,w).Any(id=>!ids.Contains(id)))throw new ArgumentException("Protected wall or dependent elements");
   for(int k=0;k<2;k++)if(((LocationCurve)w.Location).get_ElementsAtJoin(k).Cast<Element>().Any(e=>!ids.Contains(e.Id)))throw new ArgumentException("Third-party end join");
  }
  Guard(a);Guard(b);var la=Axis(a);var lb=Axis(b);
  if(la.GetEndPoint(i).DistanceTo(lb.GetEndPoint(j))>Mm(.1)||Math.Abs(la.Direction.DotProduct(lb.Direction))>1e-6||Math.Abs(la.Direction.Z)>1e-6||Math.Abs(lb.Direction.Z)>1e-6)throw new ArgumentException("Coincident horizontal orthogonal endpoints required");
  var h1=a.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble()??0;var h2=b.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM)?.AsDouble()??0;
  if(h1<Mm(500)||Math.Abs(h1-h2)>Mm(.1)||Math.Abs(a.WallType.Width-b.WallType.Width)>Mm(.1)||a.WallType.Width<Mm(50))throw new ArgumentException("Equal valid unconnected heights and widths required");
  // Revit 2025 endpoint ordering can report the participating wall from one side only; verify both allow flags, at least one native membership, and physical overlap.
  bool Joined(Wall w,int k,Wall other)=>((LocationCurve)w.Location).get_ElementsAtJoin(k).Cast<Element>().Any(e=>e.Id==other.Id);
  bool a0=WallUtils.IsWallJoinAllowedAtEnd(a,i),b0=WallUtils.IsWallJoinAllowedAtEnd(b,j);
  bool aj0=Joined(a,i,b),bj0=Joined(b,j,a);
  if(JoinGeometryUtils.AreElementsJoined(d,a,b))throw new ArgumentException("Explicit geometry-joined pair outside this end-joint operation");
  double before=Overlap(a,b);
  string mode=r.GetProperty("mode").GetString()??"";
  if(mode=="inspect"){if(r.TryGetProperty("commit",out var p)&&p.ValueKind==JsonValueKind.True)throw new ArgumentException("Inspection cannot commit");return (new{firstAllowed=a0,secondAllowed=b0,firstEndJoin=aj0,secondEndJoin=bj0,overlapM3=before,widthMm=a.WallType.Width*304.8,heightMm=h1*304.8},()=>{});}
  if(mode!="join"&&mode!="separate")throw new ArgumentException("Inspect, join or separate");
  if(a0!=r.GetProperty("expectedFirstAllowed").GetBoolean()||b0!=r.GetProperty("expectedSecondAllowed").GetBoolean())throw new InvalidOperationException("Stale end-join state");
  double expected=r.GetProperty("expectedOverlapM3").GetDouble();
  if(!double.IsFinite(expected)||expected<0||Math.Abs(expected-before)>1e-7)throw new InvalidOperationException("Stale overlap");
  bool join=mode=="join";
  if(join&&(before<=1e-7||aj0||bj0||a0&&b0)||!join&&(!(aj0||bj0)||!a0||!b0||before>1e-7))throw new ArgumentException("Expected positive unjoined overlap or finished native end joint");
  if(join){WallUtils.AllowWallJoinAtEnd(a,i);WallUtils.AllowWallJoinAtEnd(b,j);}else{WallUtils.DisallowWallJoinAtEnd(a,i);WallUtils.DisallowWallJoinAtEnd(b,j);}
  d.Regenerate();
  void Verify(){
   Guard(a);Guard(b);
   for(int k=0;k<2;k++)if(Axis(a).GetEndPoint(k).DistanceTo(la.GetEndPoint(k))>Mm(.1)||Axis(b).GetEndPoint(k).DistanceTo(lb.GetEndPoint(k))>Mm(.1))throw new InvalidOperationException("Axis shifted");
   if(WallUtils.IsWallJoinAllowedAtEnd(a,i)!=join||WallUtils.IsWallJoinAllowedAtEnd(b,j)!=join||(Joined(a,i,b)||Joined(b,j,a))!=join)throw new InvalidOperationException("End join readback mismatch");
   var after=Overlap(a,b);
   if(join?after>1e-7:after<=1e-7)throw new InvalidOperationException("Physical overlap verification failed");
  }
  Verify();
  return (new{firstId=a.Id.Value,secondId=b.Id.Value,mode,beforeM3=before,afterM3=Overlap(a,b),endJoin=join,axesPreserved=true,note="Native orthogonal end join, not a visual material-finish certification."},Verify);
 }
}