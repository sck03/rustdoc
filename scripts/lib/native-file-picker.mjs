import assert from 'node:assert/strict';

// Tauri freezes its invoke function. Isolate only the chosen dialog's IPC fetch,
// leaving commands, generated HTTP clients and output publication untouched.
export async function withNativeFilePicker({run, command, destination}, action) {
  await run(`(()=>{
    if(window.__exportDocPicker)throw Error('A native picker is already isolated');
    const original=window.fetch,url=window.__TAURI_INTERNALS__.convertFileSrc(${JSON.stringify(command)},'ipc');
    const state={original,calls:0};window.__exportDocPicker=state;
    const mock=(input,init)=>{
      if((input instanceof Request?input.url:String(input))===url){
        state.calls++;return Promise.resolve(new Response(${JSON.stringify(JSON.stringify(destination))},{headers:{'Content-Type':'application/json','Tauri-Response':'ok'}}));
      }
      return original.call(window,input,init);
    };
    window.fetch=mock;if(window.fetch!==mock)throw Error('Unable to isolate native file dialog');
  })()`);
  try {
    await action();
    assert.equal(await run('window.__exportDocPicker.calls'),1,'Use the isolated native file dialog exactly once');
  } finally {
    await run('window.fetch=window.__exportDocPicker.original;delete window.__exportDocPicker;true');
  }
}
