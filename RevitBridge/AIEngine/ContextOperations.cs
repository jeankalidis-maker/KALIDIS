using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Kalidis.AIEngine;
internal static class ContextOperations {
 internal static object Read(UIApplication app,Document d,JsonElement r){
  var view=d.ActiveView;
  var selected=app.ActiveUIDocument.Selection.GetElementIds().OrderBy(x=>x.Value).ToArray();
  var limit=r.TryGetProperty("selectionLimit",out var v)?v.GetInt32():100;
  if(limit<1||limit>200)throw new ArgumentException("selectionLimit 1-200");
  var elements=selected.Take(limit).Select(id=>{
   var e=d.GetElement(id);
   return new{id=id.Value,uniqueId=e?.UniqueId,category=e?.Category?.Name,name=e?.Name,typeId=e?.GetTypeId().Value,levelId=e?.LevelId.Value,pinned=e?.Pinned};
  }).ToArray();
  return new{ok=true,revision=Application.ModelRevision,session=Application.SessionId,
   title=d.Title,path=d.PathName,readOnly=d.IsReadOnly,workshared=d.IsWorkshared,cloud=d.IsModelInCloud,family=d.IsFamilyDocument,modified=d.IsModified,
   view=new{id=view.Id.Value,name=view.Name,kind=view.ViewType.ToString(),levelId=view.GenLevel?.Id.Value},
   selectionCount=selected.Length,truncated=selected.Length>limit,elements,
   units="mm",note="Revision is a session-wide change signal, not a document version or write authorization."};
 }
}
