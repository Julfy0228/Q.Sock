import { dotnet } from './_framework/dotnet.js';
import { media, configureMedia } from './media/bridge.js';
import { scanQr } from './qr.js';
import { pluginCache, verifyPluginSignature } from './plugin-cache.js';

const { setModuleImports, runMain, getConfig, getAssemblyExports } = await dotnet
  .withResourceLoader((type, name, url, integrity) => {
    if (type === 'dotnetjs') return url;
    return fetch(url, { cache: 'reload', integrity });
  })
  .create();

setModuleImports('qsock', {
  media,
  scanQr,
  pluginCache,
  verifyPluginSignature,
  readPreference: (key) => localStorage.getItem('qsock.preference.' + key) || '',
  writePreference: (key, value) => localStorage.setItem('qsock.preference.' + key, value),
  origin: () => location.origin + '/',
  consumeLaunchCode: () => {
    const fragment = new URLSearchParams(location.hash.slice(1));
    const code = fragment.get('launch') || '';
    if (code) history.replaceState(null, '', location.pathname + location.search);
    return code;
  },
  readAccounts: () => sessionStorage.getItem('qsock.avalonia.accounts') || '[]',
  writeAccounts: (value) => sessionStorage.setItem('qsock.avalonia.accounts', value),
  deviceId: () => {
    let device = localStorage.getItem('qsock.device');
    if (!device) {
      device = crypto.randomUUID();
      localStorage.setItem('qsock.device', device);
    }
    return device;
  },
});

configureMedia((await getAssemblyExports('Q.Sock.Browser.dll')).Q.Sock.Browser.BrowserMediaExports);
await runMain(getConfig().mainAssemblyName, []);
