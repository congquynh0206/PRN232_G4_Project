(() => {
  const { $, safe } = G4;
  function tab(id) {
    if (!G4.Navigation.select(id)) return;
    if (id === 'disputes') safe(() => G4.Disputes.load());
    else if (id === 'promotions') safe(() => G4.Promotions.load());
    else if (id === 'inbox') safe(() => G4.Notifications.load());
    else if (id === 'integration-logs') safe(() => G4.IntegrationLogs.load());
    else if (id === 'platform-fees') safe(() => G4.PlatformFees.load());
    else if (id === 'seller-debts') safe(() => G4.SellerDebt.loadAdmin());
    else if (id === 'seller-risk') safe(() => G4.SellerRisk.load());
    else if (id === 'seller-levels') safe(() => G4.SellerLevels.load());
    else if (id === 'order-lookup') safe(() => G4.AdminOrderLookup.load());
  }
  G4.Disputes.init('admin');
  G4.Promotions.init('admin');
  G4.Notifications.init('admin');
  G4.IntegrationLogs.init('admin');
  G4.PlatformFees.init();
  G4.SellerDebt.init('admin');
  G4.SellerRisk.init();
  G4.SellerLevels.init('admin');
  G4.AdminOrderLookup.init();
  G4.Navigation.init(tab);
  G4.Realtime?.subscribe(['orders', 'returns', 'disputes', 'finance', 'levels', 'notifications'], async () => {
    const id = document.querySelector('.tab-panel.active')?.id;
    if (id === 'seller-debts') await G4.SellerDebt.loadAdmin();
    else if (id === 'seller-risk') await G4.SellerRisk.load();
    else if (id === 'seller-levels') await G4.SellerLevels.refresh();
    else if (id === 'inbox') await G4.Notifications.load();
  });
})();
