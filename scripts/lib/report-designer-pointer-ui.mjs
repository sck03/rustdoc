import assert from 'node:assert/strict';

export async function doubleClickDesignerNode(page, selector, read) {
  const point = await read(page, `(async()=>{
    const node=document.querySelector(${JSON.stringify(selector)});
    node.scrollIntoView({block:'center',inline:'center'});
    await new Promise(resolve=>requestAnimationFrame(()=>requestAnimationFrame(resolve)));
    const r=node.getBoundingClientRect(),x=r.x+r.width/2,y=r.y+r.height/2;
    return {x,y,reachable:node.contains(document.elementFromPoint(x,y))};
  })()`);
  assert(point.reachable, `The actual pointer must reach ${selector}`);
  await page.send('Input.dispatchMouseEvent', {type:'mouseMoved',x:point.x,y:point.y});
  for (const clickCount of [1,2]) for (const type of ['mousePressed','mouseReleased']) {
    await page.send('Input.dispatchMouseEvent', {type,x:point.x,y:point.y,button:'left',clickCount});
  }
}
