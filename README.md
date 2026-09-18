# Dota 2 Team Board

แอป Windows ขนาดเล็กสำหรับอ่าน `data/board.json` แล้วแสดงเป็นตารางพร้อมไอคอนฮีโร่แบบ offline

ตัวแอปฝัง Noto Sans Thai และ Pixelify Sans ไว้ภายใน assembly พร้อมใบอนุญาต OFL โดยใช้ Noto Sans Thai เป็นฟอนต์หลักเพื่อรองรับทั้งไทยและอังกฤษ

ไอคอนโปรแกรมสร้างจาก `42d84f9d-a5c9-441a-8c7d-81f098a4da6e.png` ด้วย `scripts/create-app-icon.ps1` และฝังไว้ในไฟล์ executable

## ใช้งานระหว่างพัฒนา

```powershell
dotnet run
```

- แก้ `data/board.json` ในโฟลเดอร์ output ขณะรัน แอปจะรีเฟรชตารางอัตโนมัติ
- กด **ซ่อน** หรือย่อหน้าต่างเพื่อเก็บแอปไว้ใน system tray
- ดับเบิลคลิกไอคอน tray เพื่อเปิดกลับมา
- กด `AUTO DATA: เปิด/ปิด` เพื่ออัปเดต patch, hero meta และ counter data ทุก 6 ชั่วโมง โดยแอปจำค่าไว้ใน `%LocalAppData%\DotaComboBoard\settings.json`
- กด `เมนู` เพื่อเปิด **แผนสู้ฮีโร่เมตา**, **ค้นหาฮีโร่แก้ทาง**, อัปเดตข้อมูลทันที หรือตั้งค่าเปิดพร้อม Windows
- ค่าเริ่มต้นเปิดเต็ม work area ของจอ 2 ที่ `864x1488` แต่สามารถลากย่อ–ขยายได้ ตารางและคอลัมน์จะปรับตามหน้าต่างอัตโนมัติ โดยตั้งค่าได้ผ่าน `targetScreen`, `fitToWorkArea` และ `lockSize` ใน JSON
- ตารางยืดและหดตามความกว้างหน้าต่างอัตโนมัติ ค่า `width` ของแต่ละคอลัมน์ใน JSON ใช้เป็นสัดส่วนเทียบกับคอลัมน์อื่น
- ตารางหลักมี 24 ชุดสาย aggressive damage โดยทุกชุดจัดเป็น `Carry + Mid + Support`
- คลิก lineup เพื่อเปิด Gemini analysis แบบหน้า `v1.3.2` ซึ่งมี phase plan, counters, replacements และ critical stage กลับมาครบ พร้อมไอเทม 4 ชิ้นต่อฮีโร่
- ลำดับดราฟต์แสดง counter ที่ต้องระวังในแต่ละ pick และฮีโร่ตำแหน่งเดียวกันที่ควรเปลี่ยนหยิบตอบโต้หากศัตรูเปิดตัวแก้ทางก่อน
- หน้า analysis ใช้แท็บชื่อฮีโร่จริง เช่น `Wraith King / Sniper / Venge` โดยแต่ละแท็บมี timeline, แผนที่ Dota 2 จากไฟล์เกมปัจจุบัน, จุดเกิด neutral camp ทั้งหมด, เส้นทางฟาร์ม Radiant/Dire 5 ช่วง และ checklist ไปต่อ/ยกเลิก 4 ข้อ
- แท็บฮีโร่ใช้ lazy loading: ถ้ายังไม่กดแท็บ แอปจะไม่เรียก Gemini และไม่โหลด build/แผนเดินเกมของฮีโร่ตัวนั้น
- แถบหัวหน้าหลักและหน้า analysis แสดง tag เวอร์ชันแอปจาก assembly (`APP v1.3.3`) และ Dota patch ล่าสุดจาก Valve
- หน้า **ค้นหาฮีโร่แก้ทาง** ใช้ธีมเดียวกับหน้าหลัก และโหมด `5v5 VERSUS` แสดงฮีโร่ครบทั้ง STR/AGI/INT/UNI พร้อมกัน 4 กลุ่มด้วยปุ่ม compact
- ผล 5v5 ระบุว่าตัวแนะนำได้เปรียบศัตรูตัวใด พร้อมเปอร์เซ็นต์ เหตุผลเชิง mechanic แบบสั้น ปุ่มกลับไปเลือกฮีโร่ และทีมสวน 3 ชุด
- ช่องค้นหาฮีโร่รองรับปุ่มค้นหา, Enter, ชื่อแบบเว้นวรรค/ขีด และแสดงจำนวนผลลัพธ์ทันที
- หน้า Settings ตรวจ Gemini key กับโมเดล `generateContent` จริง บันทึก key ที่ผ่านทันที และแสดงผลสำเร็จ/ข้อผิดพลาดชัดเจน
- เมื่อ OpenDota ไม่ตอบสนอง หน้าค้นหาและ 5v5 จะใช้ cache + Win Rate สำรองแทนการหยุดทำงาน
- หน้า **Settings / Gemini API** บันทึก key ใน `%LocalAppData%\DotaComboBoard\settings.json`, ทดสอบ key และเปิด `https://aistudio.google.com/api-keys` ได้โดยตรง
- เมนูหน้าหลัก **COMBO หลัก 3 ฮีโร่ / JSON** ใช้นำเข้า/ส่งออกเฉพาะชุด `Carry + Mid + Support` บนตารางหลัก ไม่รวมข้อมูลจาก Hero Counter หรือ 5v5
- ไฟล์ Combo Pack ฝัง Dota patch และ generation prompt ไว้ใน JSON รองรับ append แบบข้ามชุดซ้ำ, replace พร้อม backup และเปิดอ่าน `board.json` เดิมได้
- ปุ่ม `LANG: TH/EN` สลับภาษาของหน้าหลักและผลวิเคราะห์ Gemini โดยเริ่มต้นเป็นภาษาไทย ชื่อฮีโร่และไอเทมยังคงภาษาอังกฤษตามในเกม
- Gemini ใช้ fixed system prompt และ fixed JSON schema ร่วมกับ lineup ที่เลือกทุกครั้ง โดยอ่าน key จากหน้า Settings ก่อน แล้ว fallback ไป `GEMINI_API_KEY` ใน environment หรือ `.env`
- การวิเคราะห์แบ่ง fixed JSON schema เป็น Overview และรายฮีโร่; หากโมเดลหลักติด timeout/`429/5xx` แอปจะสลับใช้โมเดล Flash สำรองอัตโนมัติ
- บริบทอ่าน patch ล่าสุดจาก Valve และ item popularity จาก OpenDota พร้อม disk cache; หาก OpenDota ล่ม ระบบยังใช้ cache หรือ fallback item keys ต่อได้
- ผล Overview และแท็บฮีโร่ cache แยกกันใน `%LocalAppData%\DotaComboBoard\analysis-cache` เพื่อให้เปิดครั้งถัดไปเร็วขึ้นและไม่โหลดแท็บที่ไม่ใช้
- หน้า **แผนสู้ฮีโร่เมตา** อ่านฮีโร่ยอดนิยมจาก OpenDota รวม Underlord/Io ที่ติดตามไว้ แล้วคำนวณจาก matchup จริงว่าแผนใดใน `board.json` เหมาะที่สุด พร้อมแผนสำรอง
- หน้า **ค้นหาฮีโร่แก้ทาง** แบ่งฮีโร่ครบ 127 ตัวตาม `STR / AGI / INT / UNI` มีช่องค้นหา และแสดงทั้งตัวที่เสียเปรียบ/ได้เปรียบ
- ข้อมูล OpenDota ถูก cache 6 ชั่วโมงที่ `%LocalAppData%\DotaComboBoard\meta-cache` และใช้ cache เดิมต่อได้เมื่อบริการภายนอกชั่วคราวไม่พร้อม

## ดาวน์โหลดรูปฮีโร่ใหม่

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\download-hero-assets.ps1
```

สคริปต์อ่านรายชื่อจาก Valve Dota 2 datafeed และดาวน์โหลดรูปจาก Steam CDN ไปที่ `assets/heroes` พร้อมสร้าง `assets/heroes.json`

## สร้างไฟล์แจกใช้งาน

```powershell
dotnet publish -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true
```

ผลลัพธ์อยู่ที่ `bin/Release/net9.0-windows/win-x64/publish` โดยต้องเก็บโฟลเดอร์ `data` และ `assets` ไว้ข้างไฟล์ `.exe`

โปรเจกต์นี้เป็นเครื่องมือที่ไม่เป็นทางการและไม่มีความเกี่ยวข้องกับ Valve Corporation ชื่อและภาพ Dota 2 เป็นทรัพย์สินของเจ้าของสิทธิ์ที่เกี่ยวข้อง
