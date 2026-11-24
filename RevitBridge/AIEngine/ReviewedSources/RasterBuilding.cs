using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.RasterBuilding;
public sealed class RasterBuilding:IReviewedOperation {
 public string Name=>"raster_building";
 public object Execute(Document d,JsonElement a){
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double N(string k){double x=a.GetProperty(k).GetDouble();if(!double.IsFinite(x)||Math.Abs(x)>1000000)throw new ArgumentException(k);return Mm(x);}
  T E<T>(string k) where T:Element=>d.GetElement(new ElementId(a.GetProperty(k).GetInt64())) as T??throw new ArgumentException(k);
  var level=E<Level>("levelId");var wallType=E<WallType>("wallTypeId");var floorType=E<FloorType>("floorTypeId");var doorType=E<FamilySymbol>("doorTypeId");
  if(wallType.Kind!=WallKind.Basic||doorType.Category?.Id.Value!=(long)BuiltInCategory.OST_Doors)throw new ArgumentException("Basic wall and door type required");
  var x0=N("minXmm");var y0=N("minYmm");var x1=N("maxXmm");var y1=N("maxYmm");var x=N("dividerXmm");var y=N("doorYmm");var w=N("doorWidthMm");var h=N("heightMm");
  if(x-x0<Mm(1500)||x1-x<Mm(1500)||y1-y0<Mm(2000)||x1-x0>Mm(30000)||y1-y0>Mm(30000)||h<Mm(2200)||h>Mm(6000)||w<Mm(600)||w>Mm(1500)||y-w/2-y0<Mm(300)||y1-y-w/2<Mm(300))throw new ArgumentException("Invalid building or opening dimensions");
  var width=doorType.get_Parameter(BuiltInParameter.DOOR_WIDTH)??throw new ArgumentException("Door width unavailable");
  var height=doorType.get_Parameter(BuiltInParameter.DOOR_HEIGHT)??throw new ArgumentException("Door height unavailable");
  if(Math.Abs(width.AsDouble()-w)>Mm(0.1)||height.AsDouble()>=h)throw new ArgumentException("Selected door does not match confirmed opening width/height");
  XYZ P(double xx,double yy)=>new XYZ(xx,yy,level.ProjectElevation);
  var pts=new[]{P(x0,y0),P(x1,y0),P(x1,y1),P(x0,y1)};
  var walls=new List<Wall>();var profile=new CurveLoop();
  for(int i=0;i<4;i++){var line=Line.CreateBound(pts[i],pts[(i+1)%4]);profile.Append(line);walls.Add(Wall.Create(d,line,wallType.Id,level.Id,h,0,false,false));}
  var host=Wall.Create(d,Line.CreateBound(P(x,y0),P(x,y1)),wallType.Id,level.Id,h,0,false,false);walls.Add(host);
  if(!doorType.IsActive){doorType.Activate();d.Regenerate();}
  var door=d.Create.NewFamilyInstance(P(x,y),doorType,host,level,StructuralType.NonStructural);
  var floor=Floor.Create(d,new List<CurveLoop>{profile},floorType.Id,level.Id);
  d.Regenerate();
  var rooms=new[]{d.Create.NewRoom(level,new UV((x+x0)/2,(y0+y1)/2)),d.Create.NewRoom(level,new UV((x+x1)/2,(y0+y1)/2))};
  rooms[0].Name="Raster Room A";rooms[1].Name="Raster Room B";d.Regenerate();
  if(door.Host?.Id!=host.Id||door.Symbol.Id!=doorType.Id||rooms.Any(r=>r.Area<=0)||rooms[0].Id==rooms[1].Id)throw new InvalidOperationException("Door host or room enclosure verification failed");
  double[] expectedLengths={x1-x0,y1-y0,x1-x0,y1-y0,y1-y0};
  for(int i=0;i<walls.Count;i++)if(Math.Abs(((LocationCurve)walls[i].Location).Curve.Length-expectedLengths[i])>Mm(0.1))throw new InvalidOperationException("Wall length mismatch");
  return new{wallIds=walls.Select(z=>z.Id.Value).ToArray(),doorId=door.Id.Value,doorHostId=host.Id.Value,doorWidthMm=ToMm(width.AsDouble()),floorId=floor.Id.Value,roomIds=rooms.Select(r=>r.Id.Value).ToArray(),roomAreasM2=rooms.Select(r=>UnitUtils.ConvertFromInternalUnits(r.Area,UnitTypeId.SquareMeters)).ToArray(),wallLengthsMm=expectedLengths.Select(ToMm).ToArray(),note="Confirmed single door gap and rectangular plan only; not automatic door recognition or thickness inference."};
 }
}