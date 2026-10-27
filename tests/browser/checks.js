import { VoiceSession } from './engine.js';
import { pluginCache } from './plugin-cache.js';

const results = document.querySelector('#results');
const summary = document.querySelector('#summary');
const asynchronousErrors = [];
window.addEventListener('unhandledrejection', event => asynchronousErrors.push(event.reason?.message || String(event.reason)));
window.addEventListener('error', event => asynchronousErrors.push(event.message));

function report(label, passed) {
  const item = document.createElement('li');
  item.className = passed ? 'pass' : 'fail';
  item.textContent = `${passed ? 'PASS' : 'FAIL'}: ${label}`;
  results.append(item);
  if (!passed) throw Error(label);
}

async function eventually(condition, label) {
  const deadline = performance.now() + 15000;
  while (performance.now() < deadline) {
    if (await condition()) return;
    await new Promise(resolve => setTimeout(resolve, 50));
  }
  throw Error(`Таймаут: ${label}`);
}

function createDevices() {
  const audio = new AudioContext();
  const oscillator = audio.createOscillator();
  const destination = audio.createMediaStreamDestination();
  oscillator.connect(destination);
  oscillator.start();
  const tracks = [];
  const timers = [];
  function video(color) {
    const canvas = document.createElement('canvas');
    canvas.width = 320;
    canvas.height = 180;
    const context = canvas.getContext('2d');
    const paint = () => {
      context.fillStyle = color;
      context.fillRect(0, 0, canvas.width, canvas.height);
      context.fillStyle = '#ffffff';
      context.fillText(String(performance.now()), 20, 60);
    };
    paint();
    timers.push(setInterval(paint, 50));
    return canvas.captureStream(10).getVideoTracks()[0];
  }
  function stream(track) {
    tracks.push(track);
    return new MediaStream([track]);
  }
  return {
    async getUserMedia(constraints) {
      return stream(constraints.audio ? destination.stream.getAudioTracks()[0].clone() : video('#3977ce'));
    },
    async getDisplayMedia() {
      return stream(video('#39934e'));
    },
    async close() {
      timers.forEach(clearInterval);
      tracks.forEach(track => track.stop());
      oscillator.stop();
      await audio.close();
    },
    tracks,
  };
}

function createHubs() {
  const hubs = new Map();
  const active = new Set();
  function create(id) {
    const handlers = new Map();
    const hub = {
      connectionId: id,
      on(event, handler) { handlers.set(event, handler); },
      off(event) { handlers.delete(event); },
      async invoke(method, ...arguments_) {
        if (method === 'JoinCall') {
          const peers = [...active].map(connectionId => ({ connectionId, userId: connectionId }));
          active.add(id);
          return { peers, muted: false, canShare: true };
        }
        if (method === 'LeaveCall') {
          active.delete(id);
          for (const target of active) hubs.get(target).emit('PeerLeft', { connectionId: id });
        }
        if (method === 'Signal') {
          const [target, type, data] = arguments_;
          const payload = structuredClone({ from: id, userId: id, type, data, callId: 'synthetic-call' });
          queueMicrotask(() => hubs.get(target).emit('RtcSignal', payload));
        }
      },
      emit(event, payload) { handlers.get(event)?.(payload); },
    };
    hubs.set(id, hub);
    return hub;
  }
  return [create('a'), create('b')];
}

async function hasReceivedFrames(session, minimum) {
  for (const peer of session.peers.values()) {
    const stats = await peer.pc.getStats();
    let streams = 0;
    stats.forEach(stat => {
      if (stat.type === 'inbound-rtp' && stat.kind === 'video' && stat.framesDecoded > 0) streams++;
    });
    if (streams >= minimum) return true;
  }
  return false;
}

async function hasReceivedAudio(session) {
  for (const peer of session.peers.values()) {
    const stats = await peer.pc.getStats();
    for (const stat of stats.values()) {
      if (stat.type === 'inbound-rtp' && stat.kind === 'audio' && stat.packetsReceived > 0) return true;
    }
  }
  return false;
}

async function run() {
  const start = document.querySelector('#start');
  start.disabled = true;
  asynchronousErrors.length = 0;
  results.replaceChildren();
  summary.textContent = 'Проверка…';
  const devices = [createDevices(), createDevices()];
  const hubs = createHubs();
  const errors = [];
  const sessions = hubs.map((hub, index) => new VoiceSession(hub, () => {}, message => errors.push(message), devices[index]));
  try {
    const persisted = localStorage.getItem('qsock-browser-fixture');
    if (persisted) {
      const previous = JSON.parse(persisted);
      report('IndexedDB сохраняет пакет после перезагрузки страницы', await pluginCache.read(previous.server, previous.key) === 'AQIDBA==');
      await pluginCache.prune(previous.server, '[]');
    }
    const server = `fixture-${crypto.randomUUID()}`;
    const key = 'synthetic-package';
    await pluginCache.write(server, key, 'AQIDBA==');
    report('Запись и чтение пакета IndexedDB', await pluginCache.read(server, key) === 'AQIDBA==');
    await pluginCache.write(server, 'obsolete', 'BQ==');
    await pluginCache.prune(server, JSON.stringify([key]));
    report('Очистка удаляет только устаревший пакет', await pluginCache.read(server, 'obsolete') === '' && await pluginCache.read(server, key) === 'AQIDBA==');
    localStorage.setItem('qsock-browser-fixture', JSON.stringify({ server, key }));
    await sessions[0].join({ id: 'synthetic-call' }, { iceServers: [] });
    await sessions[1].join({ id: 'synthetic-call' }, { iceServers: [] });
    await eventually(() => sessions.every(session => [...session.peers.values()].some(peer => peer.pc.connectionState === 'connected')), 'соединение');
    report('Двустороннее соединение WebRTC без оборудования', true);
    await Promise.all(sessions.map(session => session.mic()));
    report('Синтетический микрофон включён у обоих участников', sessions.every(session => session.local.getAudioTracks()[0]?.enabled));
    await eventually(async () => await hasReceivedAudio(sessions[0]) && await hasReceivedAudio(sessions[1]), 'аудиопакеты');
    report('Аудиопакеты передаются в обе стороны', true);
    await Promise.all(sessions.map(session => session.camera()));
    await eventually(async () => await hasReceivedFrames(sessions[0], 1) && await hasReceivedFrames(sessions[1], 1), 'кадры камеры');
    report('Видео кодируется, передаётся и декодируется в обе стороны', true);
    await sessions[0].share();
    await eventually(() => hasReceivedFrames(sessions[1], 2), 'кадры демонстрации');
    report('Демонстрация передаётся одновременно с камерой', true);
    await sessions[0].moderate({ callId: 'synthetic-call', action: 'mute' });
    await sessions[0].mic();
    report('Модерация блокирует повторное включение микрофона', !sessions[0].local.getAudioTracks()[0].enabled);
    const revocation = sessions[0].moderate({ callId: 'synthetic-call', action: 'deny-share' });
    const leaving = sessions[0].leave();
    await Promise.all([revocation, leaving]);
    await eventually(() => sessions[0].screen === null, 'отзыв демонстрации');
    report('Отзыв права останавливает демонстрацию', !sessions[0].canShare);
    await Promise.all(sessions.map(session => session.leave()));
    report('Выход закрывает соединения и останавливает дорожки', sessions.every(session => session.peers.size === 0 && session.local.getTracks().length === 0) && devices.every(device => device.tracks.every(track => track.readyState === 'ended')));
    let releaseCapture;
    const delayed = new Promise(resolve => { releaseCapture = resolve; });
    const captureDevices = {
      async getUserMedia() {
        const stream = await devices[0].getUserMedia({ audio: true });
        await delayed;
        return stream;
      },
    };
    const lateSession = new VoiceSession(createHubs()[0], () => {}, message => errors.push(message), captureDevices);
    try {
      await lateSession.join({ id: 'synthetic-call' }, { iceServers: [] });
      const pending = lateSession.mic();
      await lateSession.leave();
      releaseCapture();
      await pending;
      report('Завершённый после выхода захват сразу останавливается', lateSession.local.getTracks().length === 0 && devices[0].tracks.every(track => track.readyState === 'ended'));
    } finally {
      releaseCapture();
      await lateSession.dispose();
    }
    report('Нет ошибок согласования WebRTC', errors.every(message => message === 'Микрофон ограничен модератором.'));
    await new Promise(resolve => setTimeout(resolve, 100));
    report('Нет необработанных ошибок при одновременном отзыве доступа и выходе', asynchronousErrors.length === 0);
    summary.textContent = 'PASS — все проверки пройдены. Можно перезагрузить страницу и проверить постоянный кэш.';
  } catch (error) {
    summary.textContent = `FAIL — ${error.message}`;
    console.error(error, errors);
  } finally {
    await Promise.all(sessions.map(session => session.dispose()));
    await Promise.all(devices.map(device => device.close()));
    start.disabled = false;
  }
}

document.querySelector('#start').addEventListener('click', run);
