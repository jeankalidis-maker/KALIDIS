using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.WallGapRepair;
public sealed class WallGapRepair:IReviewedOperation {
 public string Name=>"wall_gap_repair";
 public object Execute(Document d,JsonElement a){
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double Out(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double[] V(XYZ p)=>new[]{Out(p.X),Out(p.Y),Out(p.Z)};
  Wall W(string k)=>d.GetElement(new ElementId(a.GetProperty(k).GetInt64())) as Wall??throw new ArgumentException(k);
  var w=W("id");var anchor=W("anchorId");if(w.Id==anchor.Id||w.LevelId!=anchor.LevelId)throw new ArgumentException("Distinct walls on same level");
  Line L(Wall q)=>q.Location is LocationCurve c&&c.Curve is Line l?q.WallType.Kind==WallKind.Basic?l:throw new ArgumentException("Basic walls only"):throw new ArgumentException("Straight walls only");
  var original=L(w);var fixedLine=L(anchor);
  if(w.Pinned||w.GroupId!=ElementId.InvalidElementId||w.StructuralUsage!=StructuralWallUsage.NonBearing||w.FindInserts(true,true,true,true).Count!=0||w.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED)?.AsInteger()!=0||w.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger()!=0||JoinGeometryUtils.GetJoinedElements(d,w).Count!=0)throw new ArgumentException("Wall has structural or other protected dependencies");
  var loc=(LocationCurve)w.Location;
  for(int i=0;i<2;i++)if(loc.get_ElementsAtJoin(i).Cast<Element>().Any(e=>e.Id!=w.Id))throw new ArgumentException("Wall already participates in end join");
  var deps=w.GetDependentElements(null).Select(i=>d.GetElement(i)).Where(e=>e!=null&&e.Id!=w.Id).ToArray();
  if(deps.Any(e=>e is Dimension||e is IndependentTag||e is FamilyInstance||e is Opening||e is Wall||e is Sketch))throw new ArgumentException("Protected dependent elements");
  int end=a.GetProperty("end").GetInt32(),other=a.GetProperty("anchorEnd").GetInt32();
  if(end<0||end>1||other<0||other>1)throw new ArgumentException("Endpoint 0 or 1");
  var current=original.GetEndPoint(end);var kept=original.GetEndPoint(1-end);var target=fixedLine.GetEndPoint(other);
  var expected=a.GetProperty("expectedMm").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
  if(expected.Length!=3||expected.Any(v=>!double.IsFinite(v))||current.DistanceTo(new XYZ(Mm(expected[0]),Mm(expected[1]),Mm(expected[2])))>Mm(.1))throw new InvalidOperationException("Stale endpoint");
  double limit=a.GetProperty("maxGapMm").GetDouble();
  if(!double.IsFinite(limit)||limit<1||limit>500||current.DistanceTo(target)>Mm(limit)||current.DistanceTo(target)<Mm(.1))throw new ArgumentException("Gap outside bounded repair range");
  var dir=(current-kept).Normalize();var revised=target-kept;
  if(revised.GetLength()<Mm(500)||revised.Normalize().DotProduct(dir)<.9999999||Math.Abs(target.Z-current.Z)>Mm(.1))throw new ArgumentException("Only collinear extension/trimming permitted");
  if(Math.Abs(original.Direction.DotProduct(fixedLine.Direction))>.00001)throw new ArgumentException("Only orthogonal junction");
  string mode=a.GetProperty("mode").GetString()??"";
  var distance=Out(current.DistanceTo(target));
  if(mode=="diagnose")return new{id=w.Id.Value,anchorId=anchor.Id.Value,gapMm=distance,fromMm=V(current),toMm=V(target),newLengthMm=Out(revised.GetLength())};
  if(mode!="repair")throw new ArgumentException("diagnose or repair");
  // Prevent automatic join from moving the fixed wall; leave join explicitly disallowed at repaired endpoint.
  WallUtils.DisallowWallJoinAtEnd(w,end);
  loc.Curve=end==0?Line.CreateBound(target,kept):Line.CreateBound(kept,target);
  d.Regenerate();
  var actual=L(w);var after=L(anchor);
  if(actual.GetEndPoint(end).DistanceTo(target)>Mm(.1)||actual.GetEndPoint(1-end).DistanceTo(kept)>Mm(.1)||Enumerable.Range(0,2).Any(i=>after.GetEndPoint(i).DistanceTo(fixedLine.GetEndPoint(i))>Mm(.1)))throw new InvalidOperationException("Endpoint readback failed");
  return new{id=w.Id.Value,anchorId=anchor.Id.Value,beforeGapMm=distance,afterGapMm=Out(actual.GetEndPoint(end).DistanceTo(after.GetEndPoint(other))),lengthMm=Out(actual.Length),anchorPreserved=true,note="Centerline repair only, automatic join disabled at repaired endpoint; not physical finish or full dependency certification."};
 }
}