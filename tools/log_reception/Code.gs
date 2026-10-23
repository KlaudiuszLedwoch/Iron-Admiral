// Iron Admiral - log reception (Google Apps Script, account klod76@gmail.com)
// Receives game logs sent by players who switched on "Share logs" and saves them in the Drive folder IronAdmiral_logs/<date>/.
// Deploy: Deploy > New deployment > type "Web app" > Execute as: Me > Who has access: Anyone > Deploy; copy the web app URL.
// Once after the first deployment run setup() from the editor (Run) - it creates the daily clean-up (logs older than 90 days).

const FOLDER_NAME = 'IronAdmiral_logs';
const TOKEN = 'ia-logs-v1';           // the mod sends this; not a secret - it only filters random requests
const MAX_BYTES = 3 * 1024 * 1024;    // compressed size limit per upload
const MAX_PER_HOUR = 40;              // uploads per player id per hour
const KEEP_DAYS = 90;

function doPost(e) {
  try {
    const d = JSON.parse(e.postData.contents);
    if (d.token !== TOKEN) return out('bad token');
    const id = String(d.id || 'anon').replace(/[^A-Za-z0-9-]/g, '').slice(0, 40) || 'anon';
    const kind = String(d.kind || 'log').replace(/[^a-z]/g, '').slice(0, 12) || 'log';
    const ver = String(d.ver || '').replace(/[^0-9A-Za-z.\-]/g, '').slice(0, 30);
    const cache = CacheService.getScriptCache();
    const key = 'n_' + id; const n = Number(cache.get(key) || 0);
    if (n >= MAX_PER_HOUR) return out('rate limit');
    cache.put(key, String(n + 1), 3600);
    const bytes = Utilities.base64Decode(d.gz || '');
    if (bytes.length === 0 || bytes.length > MAX_BYTES) return out('size');
    const text = Utilities.ungzip(Utilities.newBlob(bytes, 'application/x-gzip', 'log.gz'));
    const now = new Date();
    const day = Utilities.formatDate(now, 'Europe/Warsaw', 'yyyy-MM-dd');
    const time = Utilities.formatDate(now, 'Europe/Warsaw', 'HHmmss');
    const dir = folder(folder(DriveApp.getRootFolder(), FOLDER_NAME), day);
    dir.createFile(text.setName(time + '_' + kind + '_' + ver + '_' + id.slice(0, 8) + '.txt').setContentType('text/plain'));
    return out('ok');
  } catch (err) {
    return out('error ' + err);
  }
}

function doGet() { return out('Iron Admiral log reception is running'); }

function folder(parent, name) {
  const it = parent.getFoldersByName(name);
  return it.hasNext() ? it.next() : parent.createFolder(name);
}

function out(s) { return ContentService.createTextOutput(s); }

// daily: remove day folders older than KEEP_DAYS
function cleanup() {
  const it = DriveApp.getRootFolder().getFoldersByName(FOLDER_NAME);
  if (!it.hasNext()) return;
  const root = it.next();
  const limit = new Date(Date.now() - KEEP_DAYS * 86400000);
  const days = root.getFolders();
  while (days.hasNext()) {
    const f = days.next();
    const d = new Date(f.getName() + 'T00:00:00');
    if (!isNaN(d) && d < limit) f.setTrashed(true);
  }
}

function setup() {
  ScriptApp.getProjectTriggers().forEach(t => { if (t.getHandlerFunction() === 'cleanup') ScriptApp.deleteTrigger(t); });
  ScriptApp.newTrigger('cleanup').timeBased().everyDays(1).atHour(4).create();
  folder(DriveApp.getRootFolder(), FOLDER_NAME);
}
