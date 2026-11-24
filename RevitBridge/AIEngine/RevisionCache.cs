using System.Diagnostics;
namespace Kalidis.AIEngine;
// UI-thread-only cache. Values are detached DTOs, not live Revit elements.
internal sealed class RevisionCache<TDocument,TData> where TDocument:class where TData:class {
 private WeakReference<TDocument>? document;
 private string? path;
 private long revision,storedAt;
 private TData? data;
 private readonly long ttl;
 internal RevisionCache(TimeSpan maxAge){if(maxAge<=TimeSpan.Zero)throw new ArgumentException("Positive max age required");ttl=(long)(maxAge.TotalSeconds*Stopwatch.Frequency);}
 internal (TData Data,bool Hit) Get(TDocument doc,string currentPath,long currentRevision,Func<TData> load){
  long now=Stopwatch.GetTimestamp();
  if(data!=null&&document!=null&&document.TryGetTarget(out var prior)&&ReferenceEquals(prior,doc)&&
     string.Equals(path,currentPath,StringComparison.OrdinalIgnoreCase)&&revision==currentRevision&&now-storedAt<ttl)
   return(data,true);
  var next=load();document=new(doc);path=currentPath;revision=currentRevision;storedAt=Stopwatch.GetTimestamp();data=next;return(next,false);
 }
}
