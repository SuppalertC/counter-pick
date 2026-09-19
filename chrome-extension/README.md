# Dota Combo Board Web Sync

1. เปิด `chrome://extensions` ใน Google Chrome
2. เปิด **Developer mode**
3. กด **Load unpacked** แล้วเลือกโฟลเดอร์ `chrome-extension`
4. ในแอปกด tag `WEB: WAITING` และคัดลอกรหัสจับคู่ 6 หลัก
5. เปิด popup ของ extension ใส่รหัส แล้วกด **CONNECT**
6. เมื่อ tag ในแอปเป็น `WEB: LINKED` ให้ค้นจากหน้าต่าง Browser Search Sync ได้

Extension เชื่อมต่อเฉพาะ `ws://127.0.0.1:37821/dota-sync` และค้นผลเว็บสาธารณะผ่าน Bing RSS เท่านั้น ไม่อ่าน cookie/session และไม่ควบคุม `chatgpt.com`.
