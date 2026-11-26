const playIcon = '<svg viewBox="0 0 24 24" aria-hidden="true"><path d="M10.1 5.9 18.3 11.2Q19.5 12 18.3 12.8L10.1 18.1Q8.5 19.1 8.5 17.2V6.8Q8.5 4.9 10.1 5.9Z" fill="currentColor"/></svg>';
const pauseIcon = '<svg viewBox="0 0 24 24" aria-hidden="true"><rect x="7" y="5" width="3.5" height="14" rx="1.5" fill="currentColor"/><rect x="14" y="5" width="3.5" height="14" rx="1.5" fill="currentColor"/></svg>';
const clock = seconds => `${Math.floor(seconds / 60)}:${String(Math.floor(seconds % 60)).padStart(2, '0')}`;
document.querySelectorAll('video').forEach((video, index) => {
  const player = document.createElement('div');
  player.className = 'media-player';
  video.before(player);
  player.append(video);
  video.controls = false;
  video.preload = 'metadata';
  video.tabIndex = 0;
  const controls = document.createElement('div');
  controls.className = 'media-controls';
  controls.innerHTML = `<button class="media-toggle" type="button" aria-label="Play video ${index + 1}">${playIcon}</button><input class="media-seek" type="range" min="0" max="100" step="0.01" value="0" aria-label="Seek video ${index + 1}" disabled><button class="media-fullscreen" type="button" aria-label="Fullscreen video ${index + 1}"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M8 4H6a2 2 0 0 0-2 2v2m12-4h2a2 2 0 0 1 2 2v2M4 16v2a2 2 0 0 0 2 2h2m12-4v2a2 2 0 0 1-2 2h-2"/></svg></button>`;
  player.append(controls);
  const toggle = controls.querySelector('.media-toggle');
  const seek = controls.querySelector('.media-seek');
  const fullscreen = controls.querySelector('.media-fullscreen');
  const label = key => translate(key).replace('{n}', index + 1);
  document.addEventListener('site-languagechange', () => {
    seek.setAttribute('aria-label', label('Seek within video {n}'));
    fullscreen.setAttribute('aria-label', label(document.fullscreenElement === player ? 'Exit fullscreen for video {n}' : 'Show video {n} in fullscreen'));
    update();
  });
  seek.setAttribute('aria-label', label('Seek within video {n}'));
  fullscreen.setAttribute('aria-label', label('Show video {n} in fullscreen'));
  let frame = 0;
  function update() {
    cancelAnimationFrame(frame);
    const duration = Number.isFinite(video.duration) ? video.duration : 0;
    seek.disabled = !duration;
    seek.max = duration || 100;
    seek.value = video.currentTime;
    seek.style.setProperty('--played', `${duration ? video.currentTime / duration * 100 : 0}%`);
    seek.setAttribute('aria-valuetext', `${clock(video.currentTime)} / ${clock(duration)}`);
    toggle.innerHTML = video.paused ? playIcon : pauseIcon;
    toggle.setAttribute('aria-label', label(video.paused ? (video.ended ? 'Replay video {n}' : 'Play video {n}') : 'Pause video {n}'));
    if (!video.paused && !video.ended) frame = requestAnimationFrame(update);
  }
  function togglePlayback() {
    if (video.paused) video.play().catch(() => { video.controls = true; });
    else video.pause();
  }
  toggle.addEventListener('click', togglePlayback);
  video.addEventListener('click', togglePlayback);
  video.addEventListener('keydown', event => {
    if (event.key === ' ' || event.key === 'Enter') { event.preventDefault(); togglePlayback(); }
  });
  seek.addEventListener('input', () => { video.currentTime = Number(seek.value); update(); });
  video.addEventListener('play', () => {
    document.querySelectorAll('video').forEach(other => { if (other !== video) other.pause(); });
  });
  ['play', 'pause', 'ended', 'timeupdate', 'loadedmetadata', 'seeked'].forEach(event => video.addEventListener(event, update));
  fullscreen.hidden = !document.fullscreenEnabled;
  fullscreen.addEventListener('click', async () => {
    try {
      if (document.fullscreenElement === player) await document.exitFullscreen();
      else await player.requestFullscreen();
    } catch { /* Leave the inline player available if fullscreen is unavailable. */ }
  });
  document.addEventListener('fullscreenchange', () => fullscreen.setAttribute('aria-label', label(document.fullscreenElement === player ? 'Exit fullscreen for video {n}' : 'Show video {n} in fullscreen')));
  update();
});

// Reserve space for the heading, controls and caption without cropping the recording.
const heroFigure = document.querySelector('.hero .real-demo');
function fitHeroVideo() {
  const controls = heroFigure.querySelector('.media-controls');
  const caption = heroFigure.querySelector('figcaption');
  const top = heroFigure.getBoundingClientRect().top + window.scrollY;
  const available = window.innerHeight - top - controls.offsetHeight - 10 - caption.offsetHeight - 8 - 24;
  const width = Math.min(960, heroFigure.parentElement.clientWidth, Math.max(180, available) * 1.5);
  heroFigure.style.setProperty('--hero-media-width', `${width}px`);
}
const heroObserver = new ResizeObserver(fitHeroVideo);
heroObserver.observe(document.querySelector('.hero-copy'));
heroObserver.observe(document.querySelector('.nav'));
heroObserver.observe(heroFigure.querySelector('figcaption'));
window.addEventListener('resize', fitHeroVideo);
fitHeroVideo();
