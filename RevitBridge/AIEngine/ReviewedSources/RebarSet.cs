// First-party experimental rebar set geometry, not structural design.
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.RebarSet;
public sealed class RebarSet : IReviewedOperation {
 public string Name=>"rebar_set";
 public object Execute(Document d,JsonElement a){
  var id=new ElementId(a.GetProperty("id").GetInt64());
  var bar=d.GetElement(id) as Rebar??throw new ArgumentException("Expected native rebar");
  if(bar.Pinned||bar.GroupId!=ElementId.InvalidElementId||!bar.IsRebarShapeDriven())throw new ArgumentException("Expected unpinned, ungrouped shape-driven rebar");
  if(bar.NumberOfBarPositions!=1)throw new ArgumentException("Expected one initial bar; refusing repeated edit");
  var host=d.GetElement(bar.GetHostId())??throw new ArgumentException("Missing host");
  var box=host.get_BoundingBox(null)??throw new ArgumentException("Missing host bounds");
  int count=a.GetProperty("count").GetInt32();double span=a.GetProperty("spanMm").GetDouble();
  if(count<2||count>100||!double.IsFinite(span)||span<10||span>10000)throw new ArgumentException("Invalid distribution");
  bool positive=a.GetProperty("normalSide").GetBoolean();
  double Mm(double n)=>UnitUtils.ConvertToInternalUnits(n,UnitTypeId.Millimeters);
  double ToMm(double n)=>UnitUtils.ConvertFromInternalUnits(n,UnitTypeId.Millimeters);
  var accessor=bar.GetShapeDrivenAccessor();
  var lines=bar.GetCenterlineCurves(false,false,false,MultiplanarOption.IncludeOnlyPlanarCurves,0);
  if(lines.Count!=1||lines[0] is not Line)throw new ArgumentException("Straight single-segment bar required");
  accessor.SetLayoutAsFixedNumber(count,Mm(span),positive,true,true);d.Regenerate();
  if(bar.NumberOfBarPositions!=count||bar.Quantity!=count)throw new InvalidOperationException("Quantity readback mismatch");
  var shifts=new List<double[]>();var inv=box.Transform.Inverse;
  for(int i=0;i<count;i++){
   var tr=accessor.GetBarPositionTransform(i);var delta=tr.Origin;
   foreach(var p in new[]{lines[0].GetEndPoint(0),lines[0].GetEndPoint(1)}){
    var q=inv.OfPoint(tr.OfPoint(p));
    if(q.X<box.Min.X||q.X>box.Max.X||q.Y<box.Min.Y||q.Y>box.Max.Y||q.Z<box.Min.Z||q.Z>box.Max.Z)throw new InvalidOperationException("Bar centerline outside host bounding box");
   }
   shifts.Add(new[]{ToMm(delta.X),ToMm(delta.Y),ToMm(delta.Z)});
  }
  return new{id=id.Value,hostId=host.Id.Value,quantity=bar.Quantity,positions=bar.NumberOfBarPositions,spacingMm=span/(count-1),distributionOffsetsMm=shifts,note="Experimental geometric set; bounding-box check of centerlines, not cover, anchorage or load design."};
 }
}