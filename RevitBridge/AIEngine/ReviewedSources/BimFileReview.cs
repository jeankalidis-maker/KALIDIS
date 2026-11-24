using Autodesk.Revit.DB;
using System.Text.Json;
using System.Security.Cryptography;
using Kalidis.AIEngine;
namespace Kalidis.BimFileReview;
public sealed class BimFileReview : IReviewedOperation {
 public string Name=>"bim_file_review";
 public object Execute(Document d,JsonElement a){
  string name=a.GetProperty("folder").GetString()??"";
  if(Path.GetFileName(name)!=name||!name.StartsWith("OnlineQuarantine-",StringComparison.Ordinal)||name.Length!=49)throw new ArgumentException("Dedicated quarantine folder required");
  string folder=Path.Combine(Path.GetDirectoryName(d.PathName)!,name);
  if(!Directory.Exists(folder)||File.GetAttributes(folder).HasFlag(FileAttributes.ReparsePoint))throw new ArgumentException("Invalid quarantine");
  string manifest=Path.Combine(folder,"manifest.json");
  if(!File.Exists(manifest)||File.GetAttributes(manifest).HasFlag(FileAttributes.ReparsePoint))throw new ArgumentException("Missing manifest");
  using var json=JsonDocument.Parse(File.ReadAllText(manifest));var item=json.RootElement;
  string path=Path.Combine(folder,Path.GetFileName(item.GetProperty("path").GetString()??""));
  if(!path.EndsWith(".rfa",StringComparison.OrdinalIgnoreCase)||!File.Exists(path)||File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint)||new FileInfo(path).Length>10000000)throw new ArgumentException("Invalid RFA");
  var bytes=File.ReadAllBytes(path);var hash=Convert.ToHexString(SHA256.HashData(bytes));
  if(bytes.LongLength!=item.GetProperty("bytes").GetInt64()||!hash.Equals(item.GetProperty("sha256").GetString(),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Hash or size mismatch");
  using var info=BasicFileInfo.Extract(path);
  return new{file=Path.GetFileName(path),sha256=hash,bytes=bytes.Length,format=info.Format,laterVersion=info.IsSavedInLaterVersion,currentVersion=info.IsSavedInCurrentVersion,workshared=info.IsWorkshared,loaded=false,note="Metadata only; not a security scan, family geometry review or approval to load."};
 }
}