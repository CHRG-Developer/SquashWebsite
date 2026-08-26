const date = document.querySelector('#date');
date.value = new Date().toISOString().slice(0, 10);
let selected;
let csrf;
async function csrfToken(){if(!csrf){const response=await fetch('/api/account/csrf');csrf=(await response.json()).token}return csrf}

document.querySelector('#login-button').onclick = () => document.querySelector('#login').showModal();
document.querySelector('#login-form').onsubmit = async event => {
  event.preventDefault(); const form = new FormData(event.target);
  const response = await fetch('/api/account/login', {method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':await csrfToken()},
    body:JSON.stringify({email:form.get('email'),password:form.get('password'),rememberMe:true})});
  if (!response.ok) { document.querySelector('#login-error').textContent='Sign in failed. Check your details and confirmed email.'; return; }
  document.querySelector('#login').close(); await loadDashboard(); await loadAvailability();
};
date.onchange = loadAvailability;

async function loadDashboard(){
  const response=await fetch('/api/member/dashboard'); if(!response.ok)return;
  const data=await response.json(); document.querySelector('#balance').textContent=`${data.account.creditBalanceUnits/100} credits`;
  document.querySelector('#membership').textContent=data.membership?`Active to ${new Date(data.membership.endsAtUtc).toLocaleDateString()}`:'No active membership';
  document.querySelector('#account').innerHTML=`<p><strong>${escapeHtml(data.account.firstName)} ${escapeHtml(data.account.lastName)}</strong></p><p>${data.bookings.length} upcoming booking(s) · ${data.alerts.length} cancellation alert(s)</p>`;
}
async function loadAvailability(){
  const response=await fetch(`/api/member/availability?date=${date.value}`); if(!response.ok)return;
  const slots=await response.json(), host=document.querySelector('#availability'); if(!slots.length){host.className='empty';host.textContent='The club is closed.';return}
  const courts=[...new Map(slots.map(x=>[x.courtId,x.courtName])).entries()]; const times=[...new Set(slots.map(x=>x.startsAtUtc))];
  host.className='grid';host.style.setProperty('--courts',courts.length);host.innerHTML=`<div class="cell"></div>${courts.map(x=>`<div class="cell"><strong>${escapeHtml(x[1])}</strong></div>`).join('')}`;
  times.forEach(time=>{host.insertAdjacentHTML('beforeend',`<div class="cell time">${new Date(time).toLocaleTimeString([],{hour:'2-digit',minute:'2-digit'})}</div>`);courts.forEach(c=>{const s=slots.find(x=>x.startsAtUtc===time&&x.courtId===c[0]);host.insertAdjacentHTML('beforeend',`<div class="cell ${s.isPeak?'peak':''}"><button ${s.available?'':'disabled'} data-slot='${JSON.stringify(s)}'>${s.available?`${s.costUnits/100} credits`:s.reason}</button></div>`);});});
  host.querySelectorAll('button:not(:disabled)').forEach(button=>button.onclick=()=>{selected=JSON.parse(button.dataset.slot);document.querySelector('#selected-slot').textContent=`${selected.courtName} · ${new Date(selected.startsAtUtc).toLocaleString()} · ${selected.costUnits/100} credits`;document.querySelector('#confirm').showModal();});
}
document.querySelector('#booking-form').onsubmit=async event=>{event.preventDefault();const form=new FormData(event.target),opponent=form.get('opponent').trim();const response=await fetch('/api/member/bookings',{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-TOKEN':await csrfToken()},body:JSON.stringify({courtId:selected.courtId,startsAtUtc:selected.startsAtUtc,opponentMemberId:opponent||null,paymentMode:form.get('payment')})});if(!response.ok){const error=await response.json();document.querySelector('#booking-error').textContent=error.message||'Booking failed.';return}document.querySelector('#confirm').close();await loadDashboard();await loadAvailability();};
function escapeHtml(value){const node=document.createElement('div');node.textContent=value??'';return node.innerHTML}
loadDashboard().then(loadAvailability);
