using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.CheckedOpening;
public sealed class CheckedOpening : IReviewedOperation {
 public string Name=>"checked_wall_opening";
 public object Execute(Document d,JsonElement a){
  var wall=d.GetElement(new ElementId(a.GetProperty("wallId").GetInt64())) as Wall??throw new ArgumentException("Wall required");
  if(wall.Pinned||wall.GroupId!=ElementId.InvalidElementId||wall.Location is not LocationCurve lc||lc.Curve is not Line line)throw new ArgumentException("Straight unpinned ungrouped wall required");
  double Mm(double n)=>UnitUtils.ConvertToInternalUnits(n,UnitTypeId.Millimeters);
  double N(string k){var x=a.GetProperty(k).GetDouble();if(!double.IsFinite(x)||x<0||x>30000)throw new ArgumentException("Invalid "+k);return Mm(x);}
  double offset=N("offsetMm"),width=N("widthMm"),bottom=N("bottomMm"),height=N("heightMm");
  var bb=wall.get_BoundingBox(null)??throw new ArgumentException("Missing bounds");
  if(width<Mm(100)||height<Mm(100)||offset<Mm(100)||offset+width>line.Length-Mm(100)||bottom+height>bb.Max.Z-bb.Min.Z-Mm(100))throw new ArgumentException("Opening outside supported bounds");
  var start=line.GetEndPoint(0);var dir=line.Direction;
  var p=start+dir*offset;var q=start+dir*(offset+width);
  p=new XYZ(p.X,p.Y,bb.Min.Z+bottom);q=new XYZ(q.X,q.Y,bb.Min.Z+bottom+height);
  double Volume()=>UnitUtils.ConvertFromInternalUnits(wall.get_Parameter(BuiltInParameter.HOST_VOLUME_COMPUTED).AsDouble(),UnitTypeId.CubicMeters);
  double before=Volume();
  var cut=d.Create.NewOpening(wall,p,q);d.Regenerate();
  double after=Volume();
  if(cut==null||cut.Host.Id!=wall.Id||before-after<=1e-6)throw new InvalidOperationException("Opening host or net volume not verified");
  return new{id=cut.Id.Value,wallId=wall.Id.Value,beforeM3=before,afterM3=after,removedM3=before-after,note="Geometric opening; no lintel, reinforcement or structural assessment."};
 }
}