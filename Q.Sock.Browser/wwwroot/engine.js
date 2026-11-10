export class VoiceSession {
  constructor(hub, changed, notice, devices = navigator.mediaDevices) {
    this.hub = hub;
    this.devices = devices;
    this.changed = changed;
    this.notice = notice;
    this.peers = new Map();
    this.local = new MediaStream();
    this.screen = null;
    this.call = null;
    this.mutedByModerator = false;
    this.canShare = true;
    this.captureVersion = 0;
    this.pendingCapture = new Set();
    this.handlers = {
      RtcSignal: (x) => this.receive(x).catch((e) => notice(e.message)),
      PeerLeft: (x) => this.drop(x.connectionId),
      CallEnded: (x) => {
        if (x.callId === this.call?.id) this.leave().catch((e) => notice(e.message));
      },
      CallModerated: (x) => this.moderate(x).catch((e) => notice(e.message)),
    };
    for (const [k, v] of Object.entries(this.handlers)) hub.on(k, v);
  }
  async join(call, ice) {
    await this.leave();
    this.call = call;
    try {
      this.ice = ice;
      const info = await this.hub.invoke('JoinCall', call.id);
      this.mutedByModerator = info.muted;
      this.canShare = info.canShare;
      for (const t of this.local.getAudioTracks()) t.enabled = !info.muted;
      for (const p of info.peers) this.peer(p.connectionId, p.userId);
      this.update();
    } catch (e) {
      await this.leave();
      throw e;
    }
  }
  peer(id, userId) {
    if (this.peers.has(id)) return this.peers.get(id);
    const pc = new RTCPeerConnection(this.ice);
    const item = {
      id,
      userId,
      pc,
      stream: new MediaStream(),
      screen: new MediaStream(),
      makingOffer: false,
      settingRemoteAnswer: false,
      ignoreOffer: false,
      pendingCandidates: [],
    };
    this.peers.set(id, item);
    item.senders = [
      pc.addTransceiver('audio', { direction: 'sendrecv' }).sender,
      pc.addTransceiver('video', { direction: 'sendrecv' }).sender,
      pc.addTransceiver('video', { direction: 'sendrecv' }).sender,
      pc.addTransceiver('audio', { direction: 'sendrecv' }).sender,
    ];
    Promise.all([
      this.replaceTrack(item, 0, this.local.getAudioTracks()[0] || null),
      this.replaceTrack(item, 1, this.local.getVideoTracks()[0] || null),
      this.replaceTrack(item, 2, this.screen?.getVideoTracks()[0] || null),
      this.replaceTrack(item, 3, this.screen?.getAudioTracks()[0] || null),
    ]).catch((e) => this.notice(e.message));
    pc.onicecandidate = ({ candidate }) =>
      candidate &&
      this.hub
        .invoke('Signal', id, 'candidate', candidate.toJSON())
        .catch((e) => this.notice(e.message));
    pc.ontrack = ({ track, transceiver }) => {
      const slot = pc.getTransceivers().indexOf(transceiver);
      const stream = slot === 2 || slot === 3 ? item.screen : item.stream;
      stream.addTrack(track);
      track.onended = () => {
        stream.removeTrack(track);
        this.update();
      };
      track.onmute = () => this.update();
      track.onunmute = () => this.update();
      this.update();
    };
    pc.onconnectionstatechange = () => this.update();
    pc.onnegotiationneeded = async () => {
      try {
        item.makingOffer = true;
        await pc.setLocalDescription();
        await this.hub.invoke('Signal', id, 'description', pc.localDescription.toJSON());
      } catch (e) {
        this.notice(e.message);
      } finally {
        item.makingOffer = false;
      }
    };
    return item;
  }
  async receive({ from, type, data, callId, userId }) {
    if (callId !== this.call?.id) return;
    const p = this.peer(from, userId),
      pc = p.pc;
    const polite = this.hub.connectionId.localeCompare(from) > 0;
    if (type === 'description') {
      const readyForOffer = !p.makingOffer && (pc.signalingState === 'stable' || p.settingRemoteAnswer);
      const collision = data.type === 'offer' && !readyForOffer;
      p.ignoreOffer = !polite && collision;
      if (p.ignoreOffer) return;
      p.settingRemoteAnswer = data.type === 'answer';
      try {
        await pc.setRemoteDescription(data);
      } finally {
        p.settingRemoteAnswer = false;
      }
      for (const candidate of p.pendingCandidates.splice(0)) await pc.addIceCandidate(candidate);
      if (data.type === 'offer') {
        await pc.setLocalDescription();
        await this.hub.invoke('Signal', from, 'description', pc.localDescription.toJSON());
      }
    } else if (type === 'candidate') {
      if (p.ignoreOffer) return;
      if (pc.remoteDescription) await pc.addIceCandidate(data);
      else p.pendingCandidates.push(data);
    }
  }
  async replaceTrack(peer, slot, track) {
    if (peer.pc.signalingState === 'closed') return;
    try {
      await peer.senders[slot].replaceTrack(track);
    } catch (error) {
      if (peer.pc.signalingState !== 'closed') throw error;
    }
  }
  async capture(kind, request) {
    if (this.pendingCapture.has(kind)) return null;
    this.pendingCapture.add(kind);
    const version = this.captureVersion;
    try {
      const stream = await request();
      if (
        version !== this.captureVersion ||
        !this.call ||
        (kind === 'mic' && this.mutedByModerator) ||
        (kind === 'screen' && !this.canShare)
      ) {
        for (const track of stream.getTracks()) track.stop();
        return null;
      }
      return stream;
    } finally {
      this.pendingCapture.delete(kind);
    }
  }
  async mic() {
    if (!this.call) throw Error('Сначала подключитесь к звонку.');
    if (this.mutedByModerator) {
      this.notice('Микрофон ограничен модератором.');
      return;
    }
    let t = this.local.getAudioTracks()[0];
    if (!t) {
      const stream = await this.capture('mic', () => this.devices.getUserMedia({ audio: true }));
      if (!stream) return;
      t = stream.getAudioTracks()[0];
      this.local.addTrack(t);
      for (const p of this.peers.values()) await this.replaceTrack(p, 0, t);
    } else t.enabled = !t.enabled;
    this.update();
  }
  async camera() {
    if (!this.call) throw Error('Сначала подключитесь к звонку.');
    const old = this.local.getVideoTracks()[0];
    if (old) {
      old.stop();
      this.local.removeTrack(old);
      for (const p of this.peers.values()) await this.replaceTrack(p, 1, null);
    } else {
      const stream = await this.capture('camera', () => this.devices.getUserMedia({ video: true }));
      if (!stream) return;
      const track = stream.getVideoTracks()[0];
      this.local.addTrack(track);
      for (const p of this.peers.values()) await this.replaceTrack(p, 1, track);
    }
    this.update();
  }
  async share() {
    if (!this.call) throw Error('Сначала подключитесь к звонку.');
    if (!this.canShare) throw Error('Демонстрация экрана ограничена модератором.');
    if (this.screen) {
      await this.stopShare();
      return;
    }
    if (!this.devices.getDisplayMedia)
      throw Error('Этот браузер не поддерживает захват экрана.');
    this.screen = await this.capture('screen', () => this.devices.getDisplayMedia({ video: true, audio: true }));
    if (!this.screen) return;
    const track = this.screen.getVideoTracks()[0];
    track.onended = () => this.stopShare().catch((e) => this.notice(e.message));
    for (const peer of this.peers.values()) {
      await this.replaceTrack(peer, 2, track);
      await this.replaceTrack(peer, 3, this.screen?.getAudioTracks()[0] || null);
    }
    this.update();
  }
  async stopShare() {
    if (!this.screen) return;
    for (const t of this.screen.getTracks()) t.stop();
    this.screen = null;
    for (const peer of this.peers.values()) {
      await this.replaceTrack(peer, 2, null);
      await this.replaceTrack(peer, 3, null);
    }
    this.update();
  }
  async moderate({ callId, action }) {
    if (callId !== this.call?.id) return;
    if (action === 'remove') {
      this.notice('Вы исключены из звонка.');
      await this.leave();
    }
    if (action === 'mute') {
      this.mutedByModerator = true;
      for (const t of this.local.getAudioTracks()) t.enabled = false;
    }
    if (action === 'allow-mic') this.mutedByModerator = false;
    if (action === 'deny-share') {
      this.canShare = false;
      await this.stopShare();
    }
    if (action === 'allow-share') this.canShare = true;
    this.update();
  }
  drop(id) {
    const p = this.peers.get(id);
    if (p) {
      p.pc.close();
      for (const t of p.stream.getTracks()) t.stop();
      for (const track of p.screen.getTracks()) track.stop();
      this.peers.delete(id);
      this.update();
    }
  }
  update() {
    this.changed({
      call: this.call,
      local: this.local,
      screen: this.screen,
      peers: [...this.peers.values()],
      mic: this.local.getAudioTracks().some((t) => t.enabled),
      camera: this.local.getVideoTracks().length > 0,
      sharing: !!this.screen,
    });
  }
  async leave() {
    this.captureVersion++;
    const had = this.call;
    this.call = null;
    if (had) await this.hub.invoke('LeaveCall').catch(() => {});
    for (const id of [...this.peers.keys()]) this.drop(id);
    for (const t of this.local.getTracks()) t.stop();
    for (const t of this.screen?.getTracks() || []) t.stop();
    this.local = new MediaStream();
    this.screen = null;
    this.update();
  }
  async dispose() {
    await this.leave();
    for (const [k, v] of Object.entries(this.handlers)) this.hub.off(k, v);
  }
}
