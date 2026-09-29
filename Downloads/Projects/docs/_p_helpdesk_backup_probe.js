// Verify backup -> checksum -> restore -> invariant, plus tamper rejection.
const { backupPortalData, restorePortalData } = require('/Users/mainguyenbinhtan/Downloads/Projects/Enterprise-IT-Helpdesk-Lab/internal-portal/src/backup');
const fs = require('fs'), os = require('os'), path = require('path');

(async () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'hd-'));
  // Tên file do module quyết định, không tự chọn — backupPortalData đọc
  // `db.json` trong dataDir.
  const dataFile = path.join(dir, 'db.json');
  const original = { tickets: [{ id: 'T1', title: 'a' }], assets: [], users: [] };
  fs.writeFileSync(dataFile, JSON.stringify(original));

  const b = await backupPortalData({ dataDir: dir, retention: 3 });
  console.log('backup file  :', path.basename(b.file));
  console.log('sha256 sidecar:', fs.existsSync(b.checksumFile) ? 'CREATED' : 'MISSING');
  console.log('checksum len :', b.checksum.length, 'hex chars');

  // Phá data rồi restore
  fs.writeFileSync(dataFile, JSON.stringify({ tickets: [], assets: [], users: [] }));
  await restorePortalData({ backupFile: b.file, targetFile: dataFile, overwrite: true });
  const after = JSON.parse(fs.readFileSync(dataFile, 'utf8'));
  console.log('restored     :', after.tickets.length === 1 ? 'MATCHES ORIGINAL' : 'MISMATCH');

  // Tamper: sửa nội dung file backup, GIỮ NGUYÊN sidecar cũ.
  // Đây là kịch bản thật: kẻ tấn công sửa file .json nhưng không đụng .sha256.
  // Copy cả sidecar sang tên mới, nếu không thì sidecar không tồn tại và
  // code đúng sẽ bỏ qua kiểm tra — ta sẽ tưởng mình đã kiểm tra.
  const bad = b.file + '.bad';
  const raw = fs.readFileSync(b.file, 'utf8').replace('"tickets"', '"ticketz"');
  fs.writeFileSync(bad, raw);
  fs.copyFileSync(b.checksumFile, `${bad}.sha256`);
  try {
    await restorePortalData({ backupFile: bad, targetFile: dataFile, overwrite: true });
    console.log('tamper       : NOT DETECTED  <-- would be a bug');
  } catch (e) {
    console.log('tamper       : REJECTED —', e.message.slice(0, 70));
  }

  // Trường hợp không có sidecar: vẫn phải restore được (backup cũ, công cụ khác).
  const noSidecar = b.file + '.nosidecar';
  fs.copyFileSync(b.file, noSidecar);
  try {
    const r = await restorePortalData({ backupFile: noSidecar, targetFile: dataFile + '.x', overwrite: true });
    console.log('no sidecar   : restored, reason =', r.integrity.reason);
  } catch (e) {
    console.log('no sidecar   : WRONGLY REJECTED —', e.message.slice(0, 60));
  }
})().catch((e) => { console.error('PROBE ERROR:', e.message); process.exit(1); });
