using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.MepSplit;
public sealed class MepSplit : IReviewedOperation {
 public string Name=>"mep_split";
 public object Execute(Document d,JsonElement a){
  var e=d.GetElement(new ElementId(a.GetProperty("id").GetInt64())) as MEPCurve??throw new ArgumentException("MEP curve required");
  if(e.Pinned||e.GroupId!=ElementId.InvalidElementId||e.Location is not LocationCurve lc||lc.Curve is not Line line)throw new ArgumentException("Straight unpinned ungrouped segment required");
  if(e is not Pipe&&e is not Duct)throw new ArgumentException("Only pipe or duct");
  if(e.ConnectorManager.Connectors.Cast<Connector>().Any(c=>c.ConnectorType==ConnectorType.End&&c.IsConnected))throw new ArgumentException("Use disconnected test segments");
  var f=a.GetProperty("fraction").GetDouble();if(!double.IsFinite(f)||f<0.1||f>0.9)throw new ArgumentException("Fraction 0.1-0.9");
  var point=line.Evaluate(f,true);double before=line.Length;
  var id=e is Pipe?PlumbingUtils.BreakCurve(d,e.Id,point):MechanicalUtils.BreakCurve(d,e.Id,point);
  d.Regenerate();var other=d.GetElement(id) as MEPCurve??throw new InvalidOperationException("Split returned no segment");
  double l1=((LocationCurve)e.Location).Curve.Length,l2=((LocationCurve)other.Location).Curve.Length;
  if(Math.Abs(before-l1-l2)>0.0001||other.GetTypeId()!=e.GetTypeId())throw new InvalidOperationException("Length/type mismatch");
  double Mm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  return new{originalId=e.Id.Value,newId=id.Value,originalLengthMm=Mm(before),firstLengthMm=Mm(l1),secondLengthMm=Mm(l2),note="Split only, no fitting inserted."};
 }
}