(() => {
  const {$,safe}=G4;
  function tab(id) {
    document.querySelectorAll('.tab-panel').forEach(x=>x.classList.toggle('active',x.id===id));
    document.querySelectorAll('.tabs button').forEach(x=>x.classList.toggle('active',x.dataset.tab===id));
    if(id==='disputes')safe(()=>G4.Disputes.load());
    else if(id==='promotions')safe(()=>G4.Promotions.load());
    else if(id==='inbox')safe(()=>G4.Notifications.load());
    else if(id==='integration-logs')safe(()=>G4.IntegrationLogs.load());
  }
  document.querySelectorAll('.tabs button').forEach(button=>button.onclick=()=>tab(button.dataset.tab));
  G4.Disputes.init('admin');
  G4.Promotions.init('admin');
  G4.Notifications.init('admin');
  G4.IntegrationLogs.init('admin');
})();
