using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Kalidis.AIEngine;
internal static class VisualOperations {
 internal static readonly string[] Actions={"activate_view","zoom_fit","preview_view","dynamo_open"};
 internal static object Run(UIApplication app,Document d,JsonElement r,string action,string root){
  var ui=app.ActiveUIDocument;
  if(action=="activate_view"){
   var v=d.GetElement(new ElementId(r.GetProperty("viewId").GetInt64())) as View??throw new ArgumentException("Invalid view");
   if(v.IsTemplate)throw new ArgumentException("Template is not an active view");
   ui.ActiveView=v;return new{ok=true,viewId=ui.ActiveView.Id.Value,name=ui.ActiveView.Name};
  }
  if(action=="zoom_fit"){
   var v=ui.GetOpenUIViews().FirstOrDefault(x=>x.ViewId==ui.ActiveView.Id)??throw new InvalidOperationException("No active UI view");
   v.ZoomToFit();return new{ok=true,viewId=v.ViewId.Value};
  }
  if(action=="dynamo_open"){
   bool player=r.TryGetProperty("player",out var flag)&&flag.ValueKind==JsonValueKind.True;
   var id=RevitCommandId.LookupPostableCommandId(player?PostableCommand.DynamoPlayer:PostableCommand.Dynamo);
   if(!app.CanPostCommand(id))throw new InvalidOperationException("Dynamo command currently unavailable");
   app.PostCommand(id);return new{ok=true,posted=true,note="Command queued; actual graph execution is not implemented"};
  }
  if(action=="preview_view"){
   var v=d.GetElement(new ElementId(r.GetProperty("viewId").GetInt64())) as View??throw new ArgumentException("Invalid view");
   if(v.IsTemplate||!v.CanBePrinted)throw new ArgumentException("View not printable");
   string folder=Path.Combine(root,"preview-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
   using var opts=new ImageExportOptions{ExportRange=ExportRange.SetOfViews,FilePath=Path.Combine(folder,"view"),HLRandWFViewsFileType=ImageFileType.PNG,ShadowViewsFileType=ImageFileType.PNG,ZoomType=ZoomFitType.FitToPage,PixelSize=1600};
   opts.SetViewsAndSheets(new List<ElementId>{v.Id});d.ExportImage(opts);
   var files=Directory.GetFiles(folder,"*.png");
   return new{ok=files.Length>0,viewId=v.Id.Value,files};
  }
  throw new ArgumentException("Unknown visual operation");
 }
}