export async function scanQr() {
  if (!('BarcodeDetector' in window)) {
    throw Error('Этот браузер не поддерживает распознавание с камеры. Выберите изображение с QR-кодом.');
  }
  const supported = await BarcodeDetector.getSupportedFormats();
  if (!supported.includes('qr_code')) throw Error('QR-сканер недоступен. Выберите изображение.');
  const detector = new BarcodeDetector({ formats: ['qr_code'] });
  const dialog = document.createElement('dialog');
  dialog.style.cssText = 'max-width:90vw;background:#202430;color:#eceef5;border:0;border-radius:16px;padding:20px;';
  const title = document.createElement('h2');
  title.textContent = 'Наведите камеру на QR-код темы';
  const video = document.createElement('video');
  video.muted = true;
  video.playsInline = true;
  video.style.cssText = 'display:block;width:min(600px,80vw);max-height:70vh;';
  const cancel = document.createElement('button');
  cancel.textContent = 'Отмена';
  cancel.style.cssText = 'margin-top:16px;padding:10px 20px;';
  dialog.append(title, video, cancel);
  document.body.append(dialog);
  dialog.showModal();
  let stream;
  let timer;
  let stopped = false;
  const cleanup = () => {
    stopped = true;
    clearTimeout(timer);
    stream?.getTracks().forEach((track) => track.stop());
    video.srcObject = null;
    dialog.remove();
  };
  try {
    const cancelled = new Promise((resolve) => {
      cancel.onclick = () => resolve('');
      dialog.addEventListener('cancel', () => resolve(''), { once: true });
    });
    const acquisition = navigator.mediaDevices
      .getUserMedia({ video: { facingMode: 'environment' }, audio: false })
      .then((candidate) => {
        if (stopped) candidate.getTracks().forEach((track) => track.stop());
        return candidate;
      });
    const acquired = await Promise.race([cancelled, acquisition]);
    if (typeof acquired === 'string') return '';
    stream = acquired;
    video.srcObject = stream;
    await video.play();
    const detected = new Promise((resolve, reject) => {
      const scan = async () => {
        if (stopped) return;
        try {
          const result = await detector.detect(video);
          if (result.length) resolve(result[0].rawValue);
          else timer = setTimeout(scan, 250);
        } catch (error) {
          reject(error);
        }
      };
      scan();
    });
    return await Promise.race([cancelled, detected]);
  } finally {
    cleanup();
  }
}
