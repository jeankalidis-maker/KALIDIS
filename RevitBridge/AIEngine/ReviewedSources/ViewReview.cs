using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.ViewReview;
public sealed class ViewReview : IReviewedOperation {
 public string Name=>"view_review";
 public object Execute(Document d,JsonElement a){
  var v=d.GetElement(new ElementId(a.GetProperty("viewId").GetInt64())) as View??throw new ArgumentException("View required");
  if(v.IsTemplate||!v.AreGraphicsOverridesAllowed())throw new ArgumentException("Graphics not supported");
  var action=a.GetProperty("mode").GetString();
  if(action=="color"){
   var ids=a.GetProperty("ids").EnumerateArray().Select(i=>i.GetInt64()).Distinct().ToArray();
   var rgb=a.GetProperty("rgb").EnumerateArray().Select(i=>i.GetByte()).ToArray();
   if(ids.Length<1||ids.Length>100||rgb.Length!=3)throw new ArgumentException("Invalid IDs/RGB");
   foreach(var id in ids){
    var eid=new ElementId(id);if(d.GetElement(eid)==null)throw new ArgumentException("Missing element");
    var o=v.GetElementOverrides(eid);o.SetProjectionLineColor(new Color(rgb[0],rgb[1],rgb[2]));v.SetElementOverrides(eid,o);
    var c=v.GetElementOverrides(eid).ProjectionLineColor;if(c.Red!=rgb[0]||c.Green!=rgb[1]||c.Blue!=rgb[2])throw new InvalidOperationException("Color readback failed");
   }
   return new{viewId=v.Id.Value,ids,rgb=rgb.Select(x=>(int)x).ToArray()};
  }
  if(action=="category"){
   var id=new ElementId(a.GetProperty("categoryId").GetInt64());bool hidden=a.GetProperty("hidden").GetBoolean();
   if(!v.CanCategoryBeHidden(id))throw new ArgumentException("Category cannot be hidden");
   v.SetCategoryHidden(id,hidden);if(v.GetCategoryHidden(id)!=hidden)throw new InvalidOperationException("Category readback failed");
   return new{viewId=v.Id.Value,categoryId=id.Value,hidden};
  }
  if(action=="style"){
   var name=a.GetProperty("style").GetString();var style=name switch{"Shading"=>DisplayStyle.Shading,"HLR"=>DisplayStyle.HLR,"ShadingWithEdges"=>DisplayStyle.ShadingWithEdges,_=>throw new ArgumentException("Unsupported style")};
   v.DisplayStyle=style;if(v.DisplayStyle!=style)throw new InvalidOperationException("Style mismatch");
   return new{viewId=v.Id.Value,style=v.DisplayStyle.ToString()};
  }
  throw new ArgumentException("Unknown view review mode");
 }
}