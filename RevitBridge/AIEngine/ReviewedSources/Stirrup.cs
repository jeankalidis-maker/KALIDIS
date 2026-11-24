using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.Stirrup;
public sealed class Stirrup : IReviewedOperation {
 public string Name=>"closed_stirrup";
 public object Execute(Document d,JsonElement a){
  var h=d.GetElement(new ElementId(a.GetProperty("hostId").GetInt64()))??throw new ArgumentException("Missing host");
  if(RebarHostData.GetRebarHostData(h)?.IsValidHost()!=true)throw new ArgumentException("Invalid host");
  var t=d.GetElement(new ElementId(a.GetProperty("typeId").GetInt64())) as RebarBarType??throw new ArgumentException("Bar type required");
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double N(string k){double x=a.GetProperty(k).GetDouble();if(!double.IsFinite(x)||Math.Abs(x)>1000000)throw new ArgumentException(k);return Mm(x);}
  var x=N("x");var y=N("y");var z=N("z");var w=N("widthMm");var l=N("lengthMm");
  if(w<Mm(200)||l<Mm(200))throw new ArgumentException("Invalid dimensions");
  var p=new[]{new XYZ(x,y,z),new XYZ(x+w,y,z),new XYZ(x+w,y+l,z),new XYZ(x,y+l,z)};
  var box=h.get_BoundingBox(null)??throw new ArgumentException("No host bounds");
  foreach(var v in p){var q=box.Transform.Inverse.OfPoint(v);if(q.X<box.Min.X||q.X>box.Max.X||q.Y<box.Min.Y||q.Y>box.Max.Y||q.Z<box.Min.Z||q.Z>box.Max.Z)throw new ArgumentException("Outside bounds");}
  var cs=new List<Curve>();for(int i=0;i<4;i++)cs.Add(Line.CreateBound(p[i],p[(i+1)%4]));
  var bar=Rebar.CreateFromCurves(d,RebarStyle.StirrupTie,t,null,null,h,XYZ.BasisZ,cs,RebarHookOrientation.Left,RebarHookOrientation.Right,true,true)??throw new InvalidOperationException("No stirrup created");
  d.Regenerate();var curves=bar.GetCenterlineCurves(false,false,false,MultiplanarOption.IncludeOnlyPlanarCurves,0);
  if(bar.GetHostId()!=h.Id||curves.Count<4)throw new InvalidOperationException("Host/shape mismatch");
  return new{id=bar.Id.Value,hostId=h.Id.Value,curveCount=curves.Count,arcs=curves.Count(c=>c is Arc),centerlineLengthMm=UnitUtils.ConvertFromInternalUnits(curves.Sum(c=>c.Length),UnitTypeId.Millimeters),note="Experimental closed outline, no hooks or code-compliant detailing certified."};
 }
}