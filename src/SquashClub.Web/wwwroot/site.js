const date = document.querySelector('#date');
date.value = new Date().toISOString().slice(0, 10);
let selected;
let csrf;

async function csrfToken() {
  if (!csrf) {
    const response = await fetch('/api/account/csrf');
    if (!response.ok) throw new Error('Unable to establish a secure session.');
    csrf = (await response.json()).token;
  }
  return csrf;
}

async function errorMessage(response, fallback) {
  try {
    const body = await response.json();
    if (body.message) return body.message;
    if (body.errors) return Object.values(body.errors).flat().join(' ');
  } catch { /* A proxy or server error may not have a JSON response. */ }
  return fallback;
}

document.querySelectorAll('[data-close]').forEach(button => {
  button.addEventListener('click', () => document.querySelector(`#${button.dataset.close}`).close());
});
document.querySelectorAll('dialog').forEach(dialog => {
  dialog.addEventListener('click', event => { if (event.target === dialog) dialog.close(); });
});
document.querySelector('#login-button').onclick = () => document.querySelector('#login').showModal();
document.querySelector('#register-button').onclick = () => document.querySelector('#register').showModal();

document.querySelector('#register-form').onsubmit = async event => {
  event.preventDefault();
  const submit = event.submitter;
  const message = document.querySelector('#register-message');
  submit.disabled = true;
  message.className = 'error';
  message.textContent = '';
  try {
    const form = new FormData(event.target);
    const response = await fetch('/api/account/register', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': await csrfToken() },
      body: JSON.stringify(Object.fromEntries(form.entries()))
    });
    if (!response.ok) { message.textContent = await errorMessage(response, 'Account creation failed.'); return; }
    message.className = 'success';
    message.textContent = 'Account created. Check your email to confirm it, then sign in.';
    event.target.reset();
  } catch (error) { message.textContent = error.message || 'Account creation failed.'; }
  finally { submit.disabled = false; }
};

document.querySelector('#login-form').onsubmit = async event => {
  event.preventDefault();
  const submit = event.submitter;
  const error = document.querySelector('#login-error');
  submit.disabled = true; error.textContent = '';
  try {
    const form = new FormData(event.target);
    const response = await fetch('/api/account/login', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': await csrfToken() },
      body: JSON.stringify({ email: form.get('email'), password: form.get('password'), rememberMe: true })
    });
    if (!response.ok) { error.textContent = 'Sign in failed. Check your details and confirmed email.'; return; }
    document.querySelector('#login').close();
    await Promise.all([loadDashboard(), loadAvailability()]);
  } catch (reason) { error.textContent = reason.message || 'Sign in failed.'; }
  finally { submit.disabled = false; }
};
date.onchange = loadAvailability;

async function loadDashboard() {
  const response = await fetch('/api/member/dashboard');
  if (!response.ok) return;
  const data = await response.json();
  document.querySelector('#balance').textContent = `${data.account.creditBalanceUnits / 100} credits`;
  document.querySelector('#membership').textContent = data.membership
    ? `Active to ${new Date(data.membership.endsAtUtc).toLocaleDateString()}` : 'No active membership';
  document.querySelector('#account').innerHTML = `<p><strong>${escapeHtml(data.account.firstName)} ${escapeHtml(data.account.lastName)}</strong></p><p>${data.bookings.length} upcoming booking(s) · ${data.alerts.length} cancellation alert(s)</p>`;
}

async function loadAvailability() {
  const response = await fetch(`/api/member/availability?date=${date.value}`);
  if (!response.ok) return;
  const slots = await response.json();
  const host = document.querySelector('#availability');
  if (!slots.length) { host.className = 'empty'; host.textContent = 'The club is closed.'; return; }
  const courts = [...new Map(slots.map(x => [x.courtId, x.courtName])).entries()];
  const times = [...new Set(slots.map(x => x.startsAtUtc))];
  host.className = 'grid'; host.style.setProperty('--courts', courts.length);
  host.innerHTML = `<div class="cell"></div>${courts.map(x => `<div class="cell"><strong>${escapeHtml(x[1])}</strong></div>`).join('')}`;
  times.forEach(time => {
    host.insertAdjacentHTML('beforeend', `<div class="cell time">${new Date(time).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}</div>`);
    courts.forEach(court => {
      const slot = slots.find(x => x.startsAtUtc === time && x.courtId === court[0]);
      const label = slot.available ? `${slot.costUnits / 100} credits` : slot.reason;
      const owner = slot.bookedByMemberName ? `<span class="booking-owner">${escapeHtml(slot.bookedByMemberName)}</span>` : '';
      host.insertAdjacentHTML('beforeend', `<div class="cell ${slot.isPeak ? 'peak' : ''}"><button ${slot.available ? '' : 'disabled'} data-slot="${encodeURIComponent(JSON.stringify(slot))}">${label}${owner}</button></div>`);
    });
  });
  host.querySelectorAll('button:not(:disabled)').forEach(button => button.onclick = () => {
    selected = JSON.parse(decodeURIComponent(button.dataset.slot));
    document.querySelector('#booking-error').textContent = '';
    document.querySelector('#selected-slot').textContent = `${selected.courtName} · ${new Date(selected.startsAtUtc).toLocaleString()} · ${selected.costUnits / 100} credits`;
    document.querySelector('#confirm').showModal();
  });
}

document.querySelector('#booking-form').onsubmit = async event => {
  event.preventDefault();
  const submit = document.querySelector('#confirm-booking-button');
  const error = document.querySelector('#booking-error');
  submit.disabled = true; submit.textContent = 'Booking…'; error.textContent = '';
  try {
    const form = new FormData(event.target);
    const opponent = form.get('opponent').trim();
    const response = await fetch('/api/member/bookings', {
      method: 'POST', headers: { 'Content-Type': 'application/json', 'X-CSRF-TOKEN': await csrfToken() },
      body: JSON.stringify({ courtId: selected.courtId, startsAtUtc: selected.startsAtUtc,
        opponentMemberId: opponent || null, paymentMode: form.get('payment') })
    });
    if (!response.ok) { error.textContent = await errorMessage(response, 'Booking failed.'); return; }
    document.querySelector('#confirm').close();
    event.target.reset();
    await Promise.all([loadDashboard(), loadAvailability()]);
  } catch (reason) { error.textContent = reason.message || 'Booking failed. Please refresh and check your bookings.'; }
  finally { submit.disabled = false; submit.textContent = 'Confirm booking'; }
};

function escapeHtml(value) { const node = document.createElement('div'); node.textContent = value ?? ''; return node.innerHTML; }
loadDashboard().then(loadAvailability);
