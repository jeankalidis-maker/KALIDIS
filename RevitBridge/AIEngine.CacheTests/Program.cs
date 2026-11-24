using Kalidis.AIEngine;
var cache=new RevisionCache<object,string>(TimeSpan.FromSeconds(10));
object doc=new(),other=new();int loads=0;
string Load()=> (++loads).ToString();
void Check(bool ok,string name){if(!ok)throw new Exception(name);}
Check(!cache.Get(doc,"A.rvt",0,Load).Hit,"initial miss");
Check(cache.Get(doc,"A.rvt",0,Load).Hit&&loads==1,"same revision hit");
Check(!cache.Get(doc,"A.rvt",1,Load).Hit&&loads==2,"revision invalidation");
Check(!cache.Get(doc,"B.rvt",1,Load).Hit&&loads==3,"path invalidation");
Check(!cache.Get(other,"B.rvt",1,Load).Hit&&loads==4,"document isolation");
var shortCache=new RevisionCache<object,string>(TimeSpan.FromMilliseconds(1));
shortCache.Get(doc,"A.rvt",1,Load);Thread.Sleep(30);
Check(!shortCache.Get(doc,"A.rvt",1,Load).Hit,"TTL expiration");
var stableCache=new RevisionCache<object,string>(TimeSpan.FromSeconds(10));
Check(!stableCache.Get(new object(),"C:/Sandbox/A.rvt",10,Load,true).Hit,"stable initial miss");
Check(stableCache.Get(new object(),"C:/Sandbox/A.rvt",10,Load,true).Hit,"fresh managed wrapper stable path hit");
Check(!stableCache.Get(new object(),"C:/Sandbox/B.rvt",10,Load,true).Hit,"distinct local path miss");
Check(!stableCache.Get(new object(),"C:/Sandbox/B.rvt",11,Load,true).Hit,"local revision miss");
Check(!stableCache.Get(new object(),"C:/Sandbox/B.rvt",11,Load,false).Hit,"identity policy change miss");
Check(!stableCache.Get(new object(),"",11,Load,true).Hit,"unsaved initial miss");
Check(!stableCache.Get(new object(),"",11,Load,true).Hit,"unsaved wrappers isolated");
Console.WriteLine("REVISION_CACHE_TESTS=PASS; thirteen cases");

var pending=new BatchCancellation();
Check(pending.Request(),"request accepted before execution");
try{pending.Check();throw new Exception("Missing cancellation");}catch(OperationCanceledException){}
try{pending.BeginCommit();throw new Exception("Commit after cancel");}catch(OperationCanceledException){}
var committing=new BatchCancellation();committing.Check();committing.BeginCommit();
Check(!committing.Request(),"commit cannot be interrupted");
var finished=new BatchCancellation();finished.Finish();Check(!finished.Request(),"finished cannot be canceled");
for(int i=0;i<100;i++){
 var race=new BatchCancellation();bool accepted=false,commit=false;
 Parallel.Invoke(()=>accepted=race.Request(),()=>{try{race.BeginCommit();commit=true;}catch(OperationCanceledException){}});
 Check(accepted!=commit,"cancel/commit mutually exclusive");
}
Console.WriteLine("BATCH_CANCELLATION_TESTS=PASS; 100 race trials");