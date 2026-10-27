import { VoiceSession } from './engine.js';

const sessions = new Map();
let managed;

export function configureMedia(exports) {
  managed = exports;
}

function render(session, state) {
  const streams = [];
  if (state.call) {
    streams.push({ id: 'local', name: 'Вы', color: session.localAvatarColor, stream: state.local, muted: true });
    if (state.screen) streams.push({ id: 'screen', screen: true, name: 'Ваш экран', stream: state.screen, muted: true });
    for (const peer of state.peers) {
      const name = session.names[peer.userId] || 'Участник';
      streams.push({ id: peer.id, name, color: session.avatarColors[peer.userId], stream: peer.stream, muted: false });
      if (peer.screen.getVideoTracks().some((track) => track.readyState === 'live' && !track.muted)) {
        streams.push({ id: peer.id + '-screen', screen: true, name: name + ' · экран', stream: peer.screen, muted: false });
      }
    }
  }
  const active = new Set(streams.map((item) => item.id));
  for (const [id, tile] of session.tiles) {
    if (active.has(id)) continue;
    tile.video.srcObject = null;
    tile.element.remove();
    session.tiles.delete(id);
  }
  for (const item of streams) {
    let tile = session.tiles.get(item.id);
    if (!tile) {
      const element = document.createElement('section');
      element.style.cssText = 'position:relative;background:#2b2d31;border-radius:12px;overflow:hidden;min-height:160px;display:flex;align-items:center;justify-content:center;';
      const video = document.createElement('video');
      video.autoplay = true;
      video.playsInline = true;
      video.controls = false;
      video.style.cssText = 'position:absolute;inset:0;width:100%;height:100%;object-fit:contain;';
      const label = document.createElement('span');
      label.style.cssText = 'position:absolute;bottom:10px;left:12px;color:#b5bac1;background:#232428cc;padding:6px 10px;border-radius:6px;font:12px Inter,system-ui;';
      const avatar = document.createElement('div');
      avatar.style.cssText = 'width:64px;height:64px;border-radius:50%;background:#007acc;color:white;display:grid;place-items:center;font:600 24px Inter,system-ui;';
      element.append(avatar, video, label);
      session.host.append(element);
      tile = { element, video, label, avatar };
      session.tiles.set(item.id, tile);
    }
    tile.label.textContent = item.name;
    tile.avatar.style.backgroundColor = item.color || '#405873';
    tile.avatar.textContent = Array.from(item.name)[0] || '?';
    tile.element.style.gridColumn = item.screen ? '1 / -1' : '';
    tile.element.style.aspectRatio = item.screen ? '16/9' : '16/10';
    tile.element.style.order = item.screen ? '-1' : '0';
    tile.video.muted = item.muted;
    const hasMedia = item.stream.getTracks().some((track) => track.readyState === 'live');
    const hasVideo = item.stream.getVideoTracks().some((track) => track.readyState === 'live' && !track.muted);
    tile.video.style.visibility = hasVideo ? 'visible' : 'hidden';
    tile.avatar.style.visibility = hasVideo ? 'hidden' : 'visible';
    if (!hasMedia) {
      tile.video.srcObject = null;
    } else if (tile.video.srcObject !== item.stream) {
      tile.video.srcObject = item.stream;
      tile.video.play().catch(() => {});
    }
  }
  managed.Update(session.id, JSON.stringify({
    connected: !!state.call,
    microphone: state.mic,
    camera: state.camera,
    sharing: state.sharing,
    participants: state.call ? state.peers.length + 1 : 0,
  }));
}

export const media = {
  createHost(id) {
    const host = document.createElement('div');
    host.style.cssText = 'display:grid;grid-template-columns:repeat(auto-fit,minmax(min(100%,280px),1fr));gap:16px;overflow:auto;align-content:start;width:100%;height:100%;background:#1e1f22;padding:8px;box-sizing:border-box;';
    const events = new Map();
    const hub = {
      connectionId: '',
      invoke: async (method, ...args) => JSON.parse(await managed.Invoke(id, method, JSON.stringify(args))),
      on: (name, callback) => events.set(name, callback),
      off: (name) => events.delete(name),
    };
    const session = { id, host, hub, events, names: {}, tiles: new Map() };
    session.engine = new VoiceSession(hub, (state) => render(session, state), (message) => managed.Notice(id, message));
    sessions.set(id, session);
    return host;
  },

  async command(id, command, json) {
    const session = sessions.get(id);
    if (!session) return;
    const data = JSON.parse(json);
    if (command === 'join') {
      session.hub.connectionId = data.connectionId;
      session.names = data.names;
      session.avatarColors = data.avatarColors;
      session.localAvatarColor = data.localAvatarColor;
      await session.engine.join(data.call, data.ice);
    } else if (command === 'mic') await session.engine.mic();
    else if (command === 'camera') await session.engine.camera();
    else if (command === 'share') await session.engine.share();
    else if (command === 'leave') await session.engine.leave();
    else if (command === 'dispose') {
      await session.engine.dispose();
      session.host.remove();
      sessions.delete(id);
    } else throw Error('Неизвестная команда медиамоста.');
  },

  receive(id, name, json) {
    sessions.get(id)?.events.get(name)?.(JSON.parse(json));
  },
};
