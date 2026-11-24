using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.WallJoinFinish;
public sealed class WallJoinFinish:IReviewedOperation{
 public string Name=>"wall_join_finish";
 public object Execute(Document d,JsonElement a){
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  Wall Get(string k)=>d.GetElement(new ElementId(a.GetProperty(k).GetInt64())) as Wall??throw new ArgumentException(k);
  var w=Get("id");var v=Get("anchorId");
  if(w.Id==v.Id||w.LevelId!=v.LevelId)throw new ArgumentException("Distinct walls same level");
  Line LineOf(Wall x)=>x.Location is LocationCurve l&&l.Curve is Line c?c:throw new ArgumentException("Straight walls required");
  foreach(var x in new[]{w,v}){
   if(x.Pinned||x.GroupId!=ElementId.InvalidElementId||x.WallType.Kind!=WallKind.Basic||x.StructuralUsage!=StructuralWallUsage.NonBearing||x.FindInserts(true,true,true,true).Count!=0||x.get_Parameter(BuiltInParameter.WALL_TOP_IS_ATTACHED)?.AsInteger()!=0||x.get_Parameter(BuiltInParameter.WALL_BOTTOM_IS_ATTACHED)?.AsInteger()!=0||x.GetDependentElements(null).Select(d.GetElement).Any(e=>e is Dimension||e is IndependentTag||e is FamilyInstance||e is Opening||e is Sketch))throw new ArgumentException("Protected wall");
   for(int j=0;j<2;j++)if(((LocationCurve)x.Location).get_ElementsAtJoin(j).Cast<Element>().Any(e=>e.Id!=x.Id&&e.Id!=w.Id&&e.Id!=v.Id))throw new ArgumentException("Third-party join");
  }
  var l1=LineOf(w);var l2=LineOf(v);int i=a.GetProperty("end").GetInt32(),k=a.GetProperty("anchorEnd").GetInt32();
  if(i<0||i>1||k<0||k>1||l1.GetEndPoint(i).DistanceTo(l2.GetEndPoint(k))>Mm(.1)||Math.Abs(l1.Direction.DotProduct(l2.Direction))>1e-6)throw new ArgumentException("Coincident orthogonal endpoints required");
  IEnumerable<Solid> Solids(GeometryElement g){foreach(var o in g){if(o is Solid s&&s.Volume>1e-9)yield return s;else if(o is GeometryInstance gi)foreach(var q in Solids(gi.GetInstanceGeometry()))yield return q;}}
  double Overlap(){double total=0;foreach(var s in Solids(w.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})))foreach(var t in Solids(v.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine})))total+=BooleanOperationsUtils.ExecuteBooleanOperation(s,t,BooleanOperationsType.Intersect).Volume;return UnitUtils.ConvertFromInternalUnits(total,UnitTypeId.CubicMeters);}
  var before=Overlap();WallUtils.AllowWallJoinAtEnd(w,i);WallUtils.AllowWallJoinAtEnd(v,k);d.Regenerate();
  bool endJoin=((LocationCurve)w.Location).get_ElementsAtJoin(i).Cast<Element>().Any(e=>e.Id==v.Id);
  if(!endJoin&&!JoinGeometryUtils.AreElementsJoined(d,w,v)){JoinGeometryUtils.JoinGeometry(d,w,v);d.Regenerate();}
  bool geometryJoin=JoinGeometryUtils.AreElementsJoined(d,w,v);
  if(!endJoin&&!geometryJoin)throw new InvalidOperationException("Native join absent");
  foreach(var pair in new[]{(w,l1),(v,l2)})for(int j=0;j<2;j++)if(LineOf(pair.Item1).GetEndPoint(j).DistanceTo(pair.Item2.GetEndPoint(j))>Mm(.1))throw new InvalidOperationException("Axis shifted");
  var after=Overlap();if(after>1e-7)throw new InvalidOperationException("Residual solid overlap "+after);
  return new{id=w.Id.Value,anchorId=v.Id.Value,beforeM3=before,afterM3=after,endJoin,geometryJoin,axesPreserved=true};
 }
}