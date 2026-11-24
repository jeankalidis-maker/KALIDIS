using System.Diagnostics;
namespace Kalidis.AIEngine;
// UI-thread-only cache. Values are detached DTOs, not live Revit elements.
internal sealed class RevisionCache<TDocument,TData> where TDocument:class where TData:class {
 private WeakReference<TDocument>? document;
 private string? path;
 private bool stablePathKey;
 private long revision,storedAt;
 private TData? data;
 private readonly long ttl;
 internal RevisionCache(TimeSpan maxAge){if(maxAge<=TimeSpan.Zero)throw new ArgumentException("Positive max age required");ttl=(long)(maxAge.TotalSeconds*Stopwatch.Frequency);}
 internal (TData Data,bool Hit) Get(TDocument doc,string currentPath,long currentRevision,Func<TData> load,bool stableSavedLocalPath=false){
  long now=Stopwatch.GetTimestamp();
  // Revit can return different managed wrappers for the same open native document.
  // Stable local paths are scoped by the session-wide open/close/change revision.
  bool stable=stableSavedLocalPath&&!string.IsNullOrWhiteSpace(currentPath);
  bool sameDocument=stable&&stablePathKey||(!stable&&!stablePathKey&&document!=null&&document.TryGetTarget(out var prior)&&ReferenceEquals(prior,doc));
  if(data!=null&&sameDocument&&
     string.Equals(path,currentPath,StringComparison.OrdinalIgnoreCase)&&revision==currentRevision&&now-storedAt<ttl)
   return(data,true);
  var next=load();document=new(doc);path=currentPath;stablePathKey=stable;revision=currentRevision;storedAt=Stopwatch.GetTimestamp();data=next;return(next,false);
 }
}
