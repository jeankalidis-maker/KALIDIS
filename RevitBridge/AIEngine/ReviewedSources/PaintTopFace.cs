// Explicit test-project finish assignment, preserving underlying construction material.
using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.PaintTopFace;
public sealed class PaintTopFace : IReviewedOperation {
 public string Name=>"paint_top_face";
 public object Execute(Document d,JsonElement a){
  var floor=d.GetElement(new ElementId(a.GetProperty("id").GetInt64())) as Floor??throw new ArgumentException("Expected floor");
  if(floor.Pinned||floor.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("Pinned or grouped floor");
  var faces=HostObjectUtils.GetTopFaces(floor).Select(r=>floor.GetGeometryObjectFromReference(r)).OfType<PlanarFace>().ToArray();
  if(faces.Length!=1)throw new ArgumentException("Single planar top face required");
  var face=faces[0];
  if(d.IsPainted(floor.Id,face))throw new ArgumentException("Existing finish preserved");
  var name=a.GetProperty("materialName").GetString()??throw new ArgumentException("Material name required");
  if(name.Length<1||name.Length>80)throw new ArgumentException("Invalid material name");
  var rgb=a.GetProperty("rgb").EnumerateArray().Select(x=>x.GetByte()).ToArray();
  if(rgb.Length!=3)throw new ArgumentException("Three RGB values required");
  if(new FilteredElementCollector(d).OfClass(typeof(Material)).Cast<Material>().Any(m=>m.Name.Equals(name,StringComparison.OrdinalIgnoreCase)))throw new ArgumentException("Existing material name; no overwrite");
  var id=Material.Create(d,name);var mat=(Material)d.GetElement(id);
  mat.Color=new Color(rgb[0],rgb[1],rgb[2]);
  d.Paint(floor.Id,face,id);d.Regenerate();
  if(!d.GetMaterialIdsForPaintCheck(floor, id))throw new InvalidOperationException("Paint material not returned by element");
  double area=UnitUtils.ConvertFromInternalUnits(floor.GetMaterialArea(id,true),UnitTypeId.SquareMeters);
  if(area<=0)throw new InvalidOperationException("Paint area missing");
  return new{id=floor.Id.Value,materialId=id.Value,materialName=name,paintAreaM2=area,rgb,note="Graphic material color and face paint only; render appearance assets and structural layers unchanged."};
 }
}
internal static class PaintCheck {
 internal static bool GetMaterialIdsForPaintCheck(this Document d,Floor floor,ElementId id)=>floor.GetMaterialIds(true).Contains(id);
}