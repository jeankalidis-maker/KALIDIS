using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.NativeShapes;
public sealed class NativeShapes : IReviewedOperation {
 public string Name=>"native_shapes";
 public object Execute(Document d,JsonElement a){
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double N(JsonElement p,string k){double n=p.GetProperty(k).GetDouble();if(!double.IsFinite(n)||Math.Abs(n)>1000000)throw new ArgumentException(k);return Mm(n);}
  T E<T>(string k) where T:Element => d.GetElement(new ElementId(a.GetProperty(k).GetInt64())) as T??throw new ArgumentException(k);
  var mode=a.GetProperty("mode").GetString();
  if(mode=="flip_wall"){
   var w=E<Wall>("id");if(w.Pinned||w.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("Pinned/grouped wall");
   bool before=w.Flipped;w.Flip();d.Regenerate();if(w.Flipped==before)throw new InvalidOperationException("Flip failed");
   return new{id=w.Id.Value,before,after=w.Flipped};
  }
  var l=E<Level>("levelId");XYZ P(JsonElement p)=>new XYZ(N(p,"x"),N(p,"y"),l.ProjectElevation);
  CurveLoop Loop(JsonElement arr){
   var pts=arr.EnumerateArray().Select(P).ToArray();if(pts.Length<3||pts.Length>50)throw new ArgumentException("3-50 points");
   var loop=new CurveLoop();for(int i=0;i<pts.Length;i++)loop.Append(Line.CreateBound(pts[i],pts[(i+1)%pts.Length]));return loop;
  }
  if(mode=="arc_wall"){
   var c=Arc.Create(P(a.GetProperty("start")),P(a.GetProperty("end")),P(a.GetProperty("mid")));
   var t=E<WallType>("typeId");var height=N(a,"heightMm");if(height<Mm(500)||height>Mm(10000))throw new ArgumentException("Invalid height");
   var w=Wall.Create(d,c,t.Id,l.Id,height,0,false,false);d.Regenerate();
   var actual=((LocationCurve)w.Location).Curve as Arc??throw new InvalidOperationException("Arc not retained");
   if(Math.Abs(actual.Radius-c.Radius)>Mm(0.1))throw new InvalidOperationException("Radius mismatch");
   return new{id=w.Id.Value,radiusMm=ToMm(actual.Radius),lengthMm=ToMm(actual.Length)};
  }
  if(mode=="floor_hole"){
   var outer=Loop(a.GetProperty("outer"));var inner=Loop(a.GetProperty("inner"));
   var t=E<FloorType>("typeId");var f=Floor.Create(d,new List<CurveLoop>{outer,inner},t.Id,l.Id);d.Regenerate();
   var faces=HostObjectUtils.GetTopFaces(f).Select(r=>f.GetGeometryObjectFromReference(r)).OfType<PlanarFace>().ToArray();
   if(faces.Length!=1||faces[0].EdgeLoops.Size!=2)throw new InvalidOperationException("Expected top face with inner opening");
   return new{id=f.Id.Value,loops=faces[0].EdgeLoops.Size,topAreaM2=UnitUtils.ConvertFromInternalUnits(faces[0].Area,UnitTypeId.SquareMeters)};
  }
  if(mode=="room_boundary"){
   var view=E<ViewPlan>("viewId");if(view.GenLevel?.Id!=l.Id||view.IsTemplate)throw new ArgumentException("Level plan required");
   var loop=Loop(a.GetProperty("points"));var curves=new CurveArray();foreach(var c in loop)curves.Append(c);
   var plane=SketchPlane.Create(d,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,new XYZ(0,0,l.ProjectElevation)));
   var lines=d.Create.NewRoomBoundaryLines(plane,curves,view);d.Regenerate();
   if(lines.Size!=curves.Size)throw new InvalidOperationException("Boundary count mismatch");
   return new{ids=lines.Cast<ModelCurve>().Select(x=>x.Id.Value).ToArray(),count=lines.Size};
  }
  throw new ArgumentException("Unknown mode");
 }
}