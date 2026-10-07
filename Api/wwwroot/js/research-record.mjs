export function recordResearchRun(source, kind, request, result) {
  const record=structuredClone({schemaVersion:1,completedAt:new Date().toISOString(),source,kind,request,result});
  function freeze(value){
    if(value&&typeof value==='object'){
      for(const child of Object.values(value))freeze(child);
      Object.freeze(value);
    }
    return value;
  }
  return freeze(record);
}
