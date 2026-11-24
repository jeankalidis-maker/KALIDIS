import {validateClosedProfile} from './profile-topology.mjs';
const tol=.1;
function point(p){if(!p||!Number.isFinite(p.x)||!Number.isFinite(p.y)||Math.abs(p.x)>1e6||Math.abs(p.y)>1e6)throw Error('Invalid XY point');return p}
function loop(ps){if(!Array.isArray(ps)||ps.length<3||ps.length>50)throw Error('3-50 loop vertices required');return validateClosedProfile(ps.map(point),tol)}
const cross=(a,b,c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
const dist=(a,b)=>Math.hypot(b.x-a.x,b.y-a.y);
const on=(a,b,c)=>Math.abs(cross(a,b,c))<=tol*dist(a,b)&&c.x>=Math.min(a.x,b.x)-tol&&c.x<=Math.max(a.x,b.x)+tol&&c.y>=Math.min(a.y,b.y)-tol&&c.y<=Math.max(a.y,b.y)+tol;
const hit=(a,b,c,d)=>cross(a,b,c)*cross(a,b,d)<0&&cross(c,d,a)*cross(c,d,b)<0||on(a,b,c)||on(a,b,d)||on(c,d,a)||on(c,d,b);
const edges=ps=>ps.map((p,i)=>[p,ps[(i+1)%ps.length]]);
function inside(p,poly){let yes=false;for(const [a,b] of edges(poly)){if(on(a,b,p))return false;if((a.y>p.y)!==(b.y>p.y)&&p.x<(b.x-a.x)*(p.y-a.y)/(b.y-a.y)+a.x)yes=!yes}return yes}
export function reviewNativeShape(a){
 if(a.mode==='floor_hole'){
  const area=loop(a.outer),hole=loop(a.inner);
  if(hole>=area||a.inner.some(p=>!inside(p,a.outer))||edges(a.inner).some(e=>edges(a.outer).some(f=>hit(...e,...f))))throw Error('Hole must be strictly inside outer profile');
  return {netAreaMm2:area-hole};
 }
 if(a.mode==='room_boundary')return {areaMm2:loop(a.points)};
 if(a.mode==='arc_wall'){
  const p=point(a.start),q=point(a.mid),r=point(a.end);
  if(Math.min(dist(p,q),dist(q,r),dist(p,r))<tol||Math.abs(cross(p,q,r))<=tol*dist(p,r))throw Error('Degenerate arc');
  if(!Number.isFinite(a.heightMm)||a.heightMm<500||a.heightMm>10000)throw Error('Invalid arc wall height');
 }
 return {ok:true};
}
