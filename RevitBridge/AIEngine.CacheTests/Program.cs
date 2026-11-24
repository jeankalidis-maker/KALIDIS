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
Console.WriteLine("REVISION_CACHE_TESTS=PASS; six cases");
