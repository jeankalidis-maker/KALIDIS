// Experimental sloped floor, not a native Ramp category or accessibility certification.
using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.SlopedFloor;
public sealed class SlopedFloor : IReviewedOperation {
 public string Name=>"sloped_floor";
 public object Execute(Document d,JsonElement a){
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double N(string k){var x=a.GetProperty(k).GetDouble();if(!double.IsFinite(x)||Math.Abs(x)>1000000)throw new ArgumentException("Invalid "+k);return x;}
  var l=d.GetElement(new ElementId(a.GetProperty("levelId").GetInt64())) as Level??throw new ArgumentException("Invalid level");
  var t=d.GetElement(new ElementId(a.GetProperty("typeId").GetInt64())) as FloorType??throw new ArgumentException("Invalid floor type");
  var length=N("lengthMm");var width=N("widthMm");var rise=N("riseMm");
  if(length<500||width<300||rise<=0||rise/length>0.3)throw new ArgumentException("Experimental slope range 0-30 percent");
  var start=new XYZ(Mm(N("x")),Mm(N("y")),l.ProjectElevation);
  var corners=new[]{start,start+new XYZ(Mm(length),0,0),start+new XYZ(Mm(length),Mm(width),0),start+new XYZ(0,Mm(width),0)};
  var loop=new CurveLoop();for(int i=0;i<4;i++)loop.Append(Line.CreateBound(corners[i],corners[(i+1)%4]));
  var tail=start+new XYZ(0,Mm(width/2),0);var head=tail+new XYZ(Mm(length),0,0);
  // Revit floor slope API uses rise/run. Check actual geometry and reject if mismatch.
  var floor=Floor.Create(d,new List<CurveLoop>{loop},t.Id,l.Id,false,Line.CreateBound(tail,head),rise/length);
  d.Regenerate();
  var faces=HostObjectUtils.GetTopFaces(floor).Select(r=>floor.GetGeometryObjectFromReference(r)).OfType<PlanarFace>().ToArray();
  if(faces.Length!=1)throw new InvalidOperationException("Expected single planar top surface");
  var n=faces[0].FaceNormal;
  if(Math.Abs(n.Z)<1e-8)throw new InvalidOperationException("Unexpected vertical face");
  var actual=-n.X/n.Z;
  if(Math.Abs(actual*length-rise)>0.5||Math.Abs(n.Y/n.Z)>0.0001)throw new InvalidOperationException($"Slope mismatch: actual rise {actual*length} mm");
  return new{id=floor.Id.Value,category=floor.Category?.Name,lengthMm=length,widthMm=width,riseMm=actual*length,slopePercent=actual*100,note="Sloped floor geometry, not native Ramp category or code compliance."};
 }
}