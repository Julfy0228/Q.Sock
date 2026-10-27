(function () {
  function initialize() {
    const configuration = document.getElementById('qsock-drawer-config');
    if (!configuration) return;
    const settings = JSON.parse(configuration.textContent);
    const drawer = document.querySelector('#message-drawer, [data-region="right-hand-drawer"]');
    if (!drawer || drawer.dataset.qsockMounted) return;
    drawer.dataset.qsockMounted = 'true';

    const container = document.createElement('div');
    container.style.cssText = 'height:100%;display:flex;flex-direction:column;background:#171a22';
    const link = document.createElement('a');
    link.textContent = settings.title + ' ↗';
    link.href = settings.launch;
    link.target = '_blank';
    link.rel = 'noopener';
    link.style.cssText = 'padding:12px;color:#8585ff';

    const frame = document.createElement('iframe');
    frame.title = settings.title;
    frame.allow = 'microphone; camera; display-capture';
    frame.style.cssText = 'width:100%;flex:1;border:0;min-height:400px';
    container.append(link, frame);
    const content = drawer.querySelector('[data-region="content-container"]') || drawer;
    content.replaceChildren(container);

    let loading = false;
    async function connect() {
      if (loading || frame.src) return;
      loading = true;
      try {
        const response = await fetch(settings.endpoint + '?sesskey=' + encodeURIComponent(settings.sesskey));
        if (!response.ok) throw new Error('Messenger unavailable');
        const result = await response.json();
        frame.src = result.url;
      } catch {
        link.textContent = 'Открыть мессенджер в новом окне ↗';
      } finally {
        loading = false;
      }
    }
    const observer = new IntersectionObserver(entries => {
      if (entries.some(entry => entry.isIntersecting)) connect();
    });
    observer.observe(frame);
  }

  if (document.readyState === 'loading') document.addEventListener('DOMContentLoaded', initialize);
  else initialize();
})();
