import { useEffect,useRef,useState } from "react";
import type { OaApprovalStage,OaRoutingUnit } from "../../api/index.ts";
import type { ApprovalAccount } from "./useApprovalSettings.ts";
import { ApprovalStagesEditor } from "./ApprovalStagesEditor.tsx";
type Point={x:number;y:number};
function positions(stages:OaApprovalStage[]):Map<string,Point> {
  const levels=new Map<string,number>();
  for(let pass=0;pass<stages.length;pass++)for(const node of stages)if(!levels.has(node.key)&&node.dependsOn.every(key=>levels.has(key)))levels.set(node.key,node.dependsOn.length?Math.max(...node.dependsOn.map(key=>levels.get(key)!))+1:0);
  const rows=new Map<number,number>();
  return new Map(stages.map(node=>{const level=levels.get(node.key)??0;const row=rows.get(level)??0;rows.set(level,row+1);return [node.key,node.layout??{x:40+level*240,y:40+row*120}] as const;}));
}
export function ApprovalGraphEditor(props:{stages:OaApprovalStage[];accounts:ApprovalAccount[];units:OaRoutingUnit[];onChange:(stages:OaApprovalStage[])=>void}) {
  const {stages,onChange}=props;
  const [view,setView]=useState<"list"|"graph">("list");
  const [preview,setPreview]=useState<{key:string;point:Point}|null>(null);
  const drag=useRef<{key:string;x:number;y:number;point:Point}|null>(null);
  const locations=positions(stages);
  useEffect(()=>{drag.current=null;setPreview(null);},[stages]);
  const save=(key:string,point:Point)=>onChange(stages.map(stage=>stage.key===key?{...stage,layout:{x:Math.round(Math.max(0,Math.min(10000,point.x))),y:Math.round(Math.max(0,Math.min(10000,point.y)))}}:stage));
  return <section aria-label="流程编排"><div className="office-card-actions"><button type="button" aria-pressed={view==="list"} onClick={()=>setView("list")}>步骤列表</button><button type="button" aria-pressed={view==="graph"} onClick={()=>setView("graph")}>流程图</button></div>
    {view==="graph"&&<><p className="office-muted">拖动或使用方向键移动节点；审批先后由下方前置节点设置决定。</p><div style={{overflow:"auto",maxHeight:440,border:"1px solid var(--border-color,#ddd)"}}><svg role="group" aria-label="流程节点与依赖关系" width={Math.max(800,...[...locations.values()].map(p=>p.x+240))} height={Math.max(280,...[...locations.values()].map(p=>p.y+120))}>
      {stages.flatMap(node=>node.dependsOn.map(key=>{const start=locations.get(key);const end=locations.get(node.key);if(!start||!end)return null;return <path key={`${key}:${node.key}`} d={`M ${start.x+180} ${start.y+34} L ${end.x} ${end.y+34}`} stroke="currentColor" fill="none"/>;}))}
      {stages.map(stage=>{const point=preview?.key===stage.key?preview.point:locations.get(stage.key)!;return <g key={stage.key} tabIndex={0} role="button" aria-label={`移动 ${stage.name}`} transform={`translate(${point.x},${point.y})`} style={{cursor:"move",touchAction:"none"}}
        onPointerDown={e=>{if(e.button!==0)return;e.currentTarget.setPointerCapture(e.pointerId);drag.current={key:stage.key,x:e.clientX,y:e.clientY,point};}}
        onPointerMove={e=>{const state=drag.current;if(state?.key===stage.key)setPreview({key:stage.key,point:{x:state.point.x+e.clientX-state.x,y:state.point.y+e.clientY-state.y}});}}
        onPointerUp={()=>{if(drag.current&&preview?.key===stage.key)save(stage.key,preview.point);drag.current=null;setPreview(null);}}
        onPointerCancel={()=>{drag.current=null;setPreview(null);}}
        onKeyDown={e=>{const direction:Record<string,Point>={ArrowLeft:{x:-16,y:0},ArrowRight:{x:16,y:0},ArrowUp:{x:0,y:-16},ArrowDown:{x:0,y:16}};if(e.key==="Escape"){drag.current=null;setPreview(null);}const delta=direction[e.key];if(delta){e.preventDefault();save(stage.key,{x:point.x+delta.x,y:point.y+delta.y});}}}>
        <rect width={180} height={68} rx={6} fill="var(--panel-background,#fff)" stroke="currentColor"/><text x={10} y={25} fontSize={14}>{stage.name.slice(0,12)}</text><text x={10} y={48} fontSize={12}>{stage.decision==="Ratio"?`${stage.approvalPercentage??100}% 会签`:stage.decision==="Any"?"或签":"会签"} · {stage.dueHours} 小时</text>
      </g>;})}
    </svg></div></>}
    <ApprovalStagesEditor {...props}/>
  </section>;
}
