using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.NativeTakeoff;
public sealed class NativeTakeoff : IReviewedOperation {
 public string Name=>"native_material_schedule";
 public object Execute(Document d,JsonElement a){
  var category=new ElementId(a.GetProperty("categoryId").GetInt64());
  var name=a.GetProperty("name").GetString()??throw new ArgumentException("Name required");
  if(name.Length<1||name.Length>100)throw new ArgumentException("Invalid name");
  if(new FilteredElementCollector(d).OfClass(typeof(View)).Cast<View>().Any(v=>v.Name==name))throw new ArgumentException("Name already exists");
  var s=ViewSchedule.CreateMaterialTakeoff(d,category);s.Name=name;
  var keys=new[]{BuiltInParameter.MATERIAL_NAME,BuiltInParameter.MATERIAL_AREA,BuiltInParameter.MATERIAL_VOLUME};
  var fields=new List<string>();
  foreach(var key in keys){
   var field=s.Definition.GetSchedulableFields().FirstOrDefault(f=>f.ParameterId.Value==(long)key)??throw new InvalidOperationException("Missing material field "+key);
   fields.Add(s.Definition.AddField(field).GetName());
  }
  d.Regenerate();
  return new{id=s.Id.Value,name=s.Name,fields,materialTakeoff=s.Definition.IsMaterialTakeoff};
 }
}