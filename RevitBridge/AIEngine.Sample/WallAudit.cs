using System.Text.Json;
using Autodesk.Revit.DB;
using Kalidis.AIEngine;
namespace Kalidis.Sample;
public sealed class WallAudit : IReviewedOperation {
 public string Name => "wall_audit";
 public object Execute(Document document, JsonElement arguments) {
  var walls=new FilteredElementCollector(document).OfClass(typeof(Wall)).Cast<Wall>().ToArray();
  return new { count=walls.Length, walls=walls.Take(200).Select(w=>new {id=w.Id.Value, name=w.Name, lengthMm=UnitUtils.ConvertFromInternalUnits(w.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH)?.AsDouble()??0,UnitTypeId.Millimeters)}).ToArray()};
 }
}
