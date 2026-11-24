// First-party reviewed extension: guarded translation with positional readback.
using System.Text.Json;
using Autodesk.Revit.DB;
using Kalidis.AIEngine;
namespace Kalidis.VerifiedMove;
public sealed class VerifiedMove : IReviewedOperation {
 public string Name=>"verified_move";
 public object Execute(Document d,JsonElement a){
  long id=a.GetProperty("id").GetInt64();
  var e=d.GetElement(new ElementId(id))??throw new ArgumentException("Missing element");
  if(e.Pinned||e.GroupId!=ElementId.InvalidElementId||e is not FamilyInstance fi||fi.Host!=null||fi.Location is not LocationPoint loc)throw new ArgumentException("Only unpinned, ungrouped, unhosted point family instances");
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double[] Pt(XYZ x)=>new[]{ToMm(x.X),ToMm(x.Y),ToMm(x.Z)};
  XYZ Read(string key){
   var p=a.GetProperty(key).EnumerateArray().Select(x=>x.GetDouble()).ToArray();
   if(p.Length!=3||p.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1000000))throw new ArgumentException("Invalid "+key);
   return new XYZ(Mm(p[0]),Mm(p[1]),Mm(p[2]));
  }
  var before=loc.Point;var expected=Read("expectedMm");var target=Read("targetMm");
  if(before.DistanceTo(expected)>Mm(0.1))throw new InvalidOperationException("Position changed since inspection");
  if(target.DistanceTo(before)>Mm(10000))throw new ArgumentException("Maximum translation 10m");
  ElementTransformUtils.MoveElement(d,e.Id,target-before);d.Regenerate();
  var after=((LocationPoint)e.Location).Point;
  if(after.DistanceTo(target)>Mm(0.1))throw new InvalidOperationException("Position verification failed");
  return new{id,beforeMm=Pt(before),targetMm=Pt(target),afterMm=Pt(after),errorMm=ToMm(after.DistanceTo(target)),verifiedInsideTransaction=true};
 }
}