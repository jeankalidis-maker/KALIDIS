using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.BitmapMaterial;
public sealed class BitmapMaterial : IReviewedOperation {
 public string Name=>"bitmap_material";
 public object Execute(Document d,JsonElement a){
  var source=d.GetElement(new ElementId(a.GetProperty("id").GetInt64())) as Material??throw new ArgumentException("Material required");
  var old=d.GetElement(source.AppearanceAssetId) as AppearanceAssetElement??throw new ArgumentException("Appearance required");
  var before=old.GetRenderingAsset().FindByName(Generic.GenericDiffuse) as AssetPropertyDoubleArray4d??throw new ArgumentException("Generic diffuse required");
  if(before.NumberOfConnectedProperties!=0)throw new ArgumentException("Untextured source required");
  var name=a.GetProperty("name").GetString()??throw new ArgumentException("Name required");
  var file=a.GetProperty("fileName").GetString()??throw new ArgumentException("Filename required");
  if(Path.GetFileName(file)!=file||!file.StartsWith("TextureFixture-",StringComparison.Ordinal)||!file.EndsWith(".png",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Dedicated local PNG fixture required");
  var path=Path.Combine(Path.GetDirectoryName(d.PathName)!,file);
  if(!File.Exists(path)||File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)||new FileInfo(path).Length>10000000)throw new ArgumentException("Invalid texture file");
  var mat=source.Duplicate(name);var asset=old.Duplicate(name+" Appearance");mat.AppearanceAssetId=asset.Id;
  using(var scope=new AppearanceAssetEditScope(d)){
   var edit=scope.Start(asset.Id);var diffuse=edit.FindByName(Generic.GenericDiffuse) as AssetPropertyDoubleArray4d??throw new InvalidOperationException("Missing diffuse");
   diffuse.AddConnectedAsset("UnifiedBitmap");
   var bitmap=diffuse.GetSingleConnectedAsset()??throw new InvalidOperationException("No bitmap");
   var p=bitmap.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString??throw new InvalidOperationException("No bitmap source");
   p.Value=path;scope.Commit(true);
  }
  d.Regenerate();
  var actual=((AssetPropertyDoubleArray4d)asset.GetRenderingAsset().FindByName(Generic.GenericDiffuse)).GetSingleConnectedAsset();
  var saved=(actual?.FindByName(UnifiedBitmap.UnifiedbitmapBitmap) as AssetPropertyString)?.Value;
  if(!string.Equals(saved,path,StringComparison.OrdinalIgnoreCase)||source.AppearanceAssetId!=old.Id||old.GetRenderingAsset().FindByName(Generic.GenericDiffuse).NumberOfConnectedProperties!=0)throw new InvalidOperationException("Texture/source verification failed: "+saved);
  return new{id=mat.Id.Value,assetId=asset.Id.Value,sourceId=source.Id.Value,bitmap=saved,sourcePreserved=true,note="Native bitmap appearance reference; render quality not verified."};
 }
}