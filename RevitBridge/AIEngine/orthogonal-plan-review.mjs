import {validateClosedProfile} from './profile-topology.mjs';
const tol=.1, near=(a,b)=>Math.hypot(a[0]-b[0],a[1]-b[1])<=tol;
const axis=(a,b)=>Math.abs(a[1]-b[1])<=tol?'x':Math.abs(a[0]-b[0])<=tol?'y':null;
const distance=(p,a,b)=>{const dx=b[0]-a[0],dy=b[1]-a[1],t=Math.max(0,Math.min(1,((p[0]-a[0])*dx+(p[1]-a[1])*dy)/(dx*dx+dy*dy)));return Math.hypot(p[0]-a[0]-t*dx,p[1]-a[1]-t*dy)};
const on=(p,l)=>distance(p,...l)<=tol;
const overlap=(l,m)=>{const a=axis(...l),b=axis(...m);if(a!==b)return false;const i=a==='x'?0:1,j=1-i;return Math.abs(l[0][j]-m[0][j])<=tol&&Math.min(Math.max(l[0][i],l[1][i]),Math.max(m[0][i],m[1][i]))-Math.max(Math.min(l[0][i],l[1][i]),Math.min(m[0][i],m[1][i]))>tol};
export function reviewOrthogonalPlan(a){
 const {perimeter:p,partitions,rooms,doors,heightMm}=a;
 if(heightMm<2200||heightMm>6000)throw Error('Plan height outside supported range');
 const points=[...p,...partitions.flatMap(l=>[l.start,l.end]),...rooms,...doors.map(d=>d.point)];
 if(points.some(v=>v.some(x=>!Number.isFinite(x)||Math.abs(x)>1000000)))throw Error('Coordinates outside supported range');
 const area=validateClosedProfile(p.map(([x,y])=>({x,y})),tol);
 if(area<1000000)throw Error('Perimeter smaller than 1 m2');
 const boundary=p.map((v,i)=>[v,p[(i+1)%p.length]]),lines=[...boundary,...partitions.map(l=>[l.start,l.end])];
 for(const l of lines)if(!axis(...l)||Math.hypot(l[0][0]-l[1][0],l[0][1]-l[1][1])<500)throw Error('Wall must be orthogonal and at least 500 mm');
 const inside=q=>{let yes=false;for(let i=0,j=p.length-1;i<p.length;j=i++){const [x,y]=p[i],[u,v]=p[j];if((y>q[1])!==(v>q[1])&&q[0]<(u-x)*(q[1]-y)/(v-y)+x)yes=!yes}return yes};
 for(let i=boundary.length;i<lines.length;i++){
  const l=lines[i],ax=axis(...l),k=ax==='x'?0:1,j=1-k;
  for(let m=0;m<i;m++)if(overlap(l,lines[m]))throw Error('Overlapping partition or perimeter');
  if(l.some(q=>!inside(q)&&!boundary.some(b=>on(q,b))))throw Error('Partition endpoint outside perimeter');
  const cuts=[l[0][k],l[1][k]];
  for(const b of boundary){if(axis(...b)===ax)continue;const q=[0,0];q[k]=b[0][k];q[j]=l[0][j];if(on(q,l)&&on(q,b))cuts.push(q[k])}
  cuts.sort((x,y)=>x-y);
  for(let m=1;m<cuts.length;m++){if(cuts[m]-cuts[m-1]<=tol)continue;const q=[0,0];q[k]=(cuts[m]+cuts[m-1])/2;q[j]=l[0][j];if(!inside(q))throw Error('Partition exits perimeter')}
 }
 for(let i=0;i<rooms.length;i++){const q=rooms[i];if(!inside(q)||lines.some(l=>distance(q,...l)<200))throw Error('Room seed outside or too close to wall');if(rooms.slice(0,i).some(r=>near(r,q)))throw Error('Duplicate room seed')}
 for(let i=0;i<doors.length;i++){const d=doors[i],l=lines[d.wallIndex];if(!l||d.widthMm<600||!on(d.point,l)||l.some(q=>Math.hypot(q[0]-d.point[0],q[1]-d.point[1])<d.widthMm/2+200))throw Error('Door location or width invalid');if(doors.slice(0,i).some(e=>Math.hypot(e.point[0]-d.point[0],e.point[1]-d.point[1])<(e.widthMm+d.widthMm)/2+100))throw Error('Doors too close')}
 return {areaMm2:area,walls:lines.length,rooms:rooms.length,doors:doors.length};
}
