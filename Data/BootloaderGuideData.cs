using System.Collections.Generic;

namespace PhoneRomFlashTool.Data
{
    public class BootloaderGuide
    {
        public string Brand { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Difficulty { get; set; } = ""; // Easy, Medium, Hard
        public string WaitTime { get; set; } = "";
        public string OfficialUrl { get; set; } = "";
        public List<string> Steps { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public List<string> Requirements { get; set; } = new();
    }

    public class FrpBypassGuide
    {
        public string Brand { get; set; } = "";
        public string Method { get; set; } = "";
        public string AndroidVersions { get; set; } = "";
        public string Difficulty { get; set; } = "";
        public List<string> Steps { get; set; } = new();
        public List<string> Tools { get; set; } = new();
        public string Warning { get; set; } = "";
    }

    public static class BootloaderGuideData
    {
        public static List<BootloaderGuide> GetGuides()
        {
            return new List<BootloaderGuide>
            {
                new BootloaderGuide
                {
                    Brand = "Xiaomi / Redmi / POCO",
                    Icon = "📱",
                    Difficulty = "Medium",
                    WaitTime = "7 วัน (168 ชั่วโมง)",
                    OfficialUrl = "https://en.miui.com/unlock/",
                    Requirements = new List<string>
                    {
                        "Mi Account ที่ลงทะเบียนในเครื่องแล้ว 7 วันขึ้นไป",
                        "เปิด OEM Unlocking ใน Developer Options",
                        "ติดตั้ง Mi Unlock Tool บน PC",
                        "USB Debugging เปิดอยู่"
                    },
                    Steps = new List<string>
                    {
                        "1. ไปที่ Settings > About Phone > แตะ MIUI Version 7 ครั้งเพื่อเปิด Developer Options",
                        "2. ไปที่ Settings > Additional Settings > Developer Options",
                        "3. เปิด OEM Unlocking และ USB Debugging",
                        "4. ไปที่ Mi Unlock Status และผูก Mi Account",
                        "5. ดาวน์โหลด Mi Unlock Tool จาก en.miui.com/unlock",
                        "6. ปิดเครื่อง และกดปุ่ม Volume Down + Power เข้า Fastboot Mode",
                        "7. เชื่อมต่อ USB และเปิด Mi Unlock Tool",
                        "8. ล็อกอิน Mi Account และกด Unlock",
                        "9. รอจนกว่าจะ Unlock สำเร็จ (อาจต้องรอ 7 วัน)"
                    },
                    Warnings = new List<string>
                    {
                        "ข้อมูลทั้งหมดในเครื่องจะถูกลบ!",
                        "ประกันอาจถูกยกเลิก",
                        "บางรุ่นต้องรอนานถึง 30 วัน"
                    }
                },
                new BootloaderGuide
                {
                    Brand = "Samsung",
                    Icon = "📱",
                    Difficulty = "Easy",
                    WaitTime = "ทันที",
                    OfficialUrl = "",
                    Requirements = new List<string>
                    {
                        "เปิด OEM Unlocking ใน Developer Options",
                        "USB Debugging เปิดอยู่",
                        "Odin หรือ Fastboot"
                    },
                    Steps = new List<string>
                    {
                        "1. ไปที่ Settings > About Phone > แตะ Build Number 7 ครั้ง",
                        "2. ไปที่ Settings > Developer Options > เปิด OEM Unlocking",
                        "3. ปิดเครื่อง",
                        "4. กด Volume Up + Volume Down + Power พร้อมกัน",
                        "5. กด Volume Up เพื่อยืนยัน Unlock",
                        "6. เครื่องจะ Factory Reset และ Unlock Bootloader"
                    },
                    Warnings = new List<string>
                    {
                        "ข้อมูลทั้งหมดจะถูกลบ!",
                        "Knox จะถูก Trip ถาวร (0x1)",
                        "Samsung Pay และแอพธนาคารบางตัวอาจใช้ไม่ได้",
                        "ประกันจะถูกยกเลิก"
                    }
                },
                new BootloaderGuide
                {
                    Brand = "Google Pixel / Nexus",
                    Icon = "📱",
                    Difficulty = "Easy",
                    WaitTime = "ทันที",
                    OfficialUrl = "https://source.android.com/docs/core/architecture/bootloader/locking_unlocking",
                    Requirements = new List<string>
                    {
                        "เปิด OEM Unlocking ใน Developer Options",
                        "ติดตั้ง Android SDK Platform Tools",
                        "USB Debugging เปิดอยู่"
                    },
                    Steps = new List<string>
                    {
                        "1. ไปที่ Settings > About Phone > แตะ Build Number 7 ครั้ง",
                        "2. ไปที่ Settings > Developer Options > เปิด OEM Unlocking",
                        "3. เชื่อมต่อ USB และเปิด Command Prompt",
                        "4. พิมพ์: adb reboot bootloader",
                        "5. พิมพ์: fastboot flashing unlock",
                        "6. ใช้ปุ่ม Volume เลือก Unlock และกด Power ยืนยัน"
                    },
                    Warnings = new List<string>
                    {
                        "ข้อมูลทั้งหมดจะถูกลบ!",
                        "อุปกรณ์จาก Carrier บางรายอาจ Unlock ไม่ได้"
                    }
                },
                new BootloaderGuide
                {
                    Brand = "OnePlus",
                    Icon = "📱",
                    Difficulty = "Easy",
                    WaitTime = "ทันที",
                    OfficialUrl = "",
                    Requirements = new List<string>
                    {
                        "เปิด OEM Unlocking ใน Developer Options",
                        "ติดตั้ง Android SDK Platform Tools",
                        "USB Debugging เปิดอยู่"
                    },
                    Steps = new List<string>
                    {
                        "1. ไปที่ Settings > About Phone > แตะ Build Number 7 ครั้ง",
                        "2. ไปที่ Settings > Developer Options > เปิด OEM Unlocking",
                        "3. เชื่อมต่อ USB และเปิด Command Prompt",
                        "4. พิมพ์: adb reboot bootloader",
                        "5. พิมพ์: fastboot oem unlock",
                        "6. ใช้ปุ่ม Volume เลือก Unlock และกด Power ยืนยัน"
                    },
                    Warnings = new List<string>
                    {
                        "ข้อมูลทั้งหมดจะถูกลบ!"
                    }
                },
                new BootloaderGuide
                {
                    Brand = "Sony Xperia",
                    Icon = "📱",
                    Difficulty = "Medium",
                    WaitTime = "ทันที",
                    OfficialUrl = "https://developer.sony.com/open-source/aosp-on-xperia-open-devices/get-started/unlock-bootloader",
                    Requirements = new List<string>
                    {
                        "เครื่องต้องรองรับการ Unlock (ตรวจสอบที่เว็บ Sony)",
                        "ติดตั้ง Android SDK Platform Tools",
                        "รหัส Unlock จาก Sony Developer"
                    },
                    Steps = new List<string>
                    {
                        "1. ไปที่ Settings > About Phone > แตะ Build Number 7 ครั้ง",
                        "2. ตรวจสอบว่าเครื่องรองรับ: พิมพ์ *#*#7378423#*#* แล้วดู Service Info > Configuration",
                        "3. ถ้าขึ้น 'Bootloader unlock allowed: Yes' ถึงจะทำได้",
                        "4. ไปที่ developer.sony.com และขอรหัส Unlock",
                        "5. กรอก IMEI และรับรหัส Unlock ทางอีเมล",
                        "6. เปิด Command Prompt และพิมพ์: adb reboot bootloader",
                        "7. พิมพ์: fastboot oem unlock 0x[รหัสที่ได้รับ]"
                    },
                    Warnings = new List<string>
                    {
                        "ข้อมูลทั้งหมดจะถูกลบ!",
                        "กล้องอาจเสีย DRM และคุณภาพลดลง",
                        "ประกันจะถูกยกเลิก"
                    }
                },
                new BootloaderGuide
                {
                    Brand = "Motorola",
                    Icon = "📱",
                    Difficulty = "Medium",
                    WaitTime = "ทันที",
                    OfficialUrl = "https://motorola-global-portal.custhelp.com/app/standalone/bootloader/unlock-your-device-a",
                    Requirements = new List<string>
                    {
                        "เครื่องต้องรองรับการ Unlock",
                        "Motorola Account",
                        "ติดตั้ง Android SDK Platform Tools"
                    },
                    Steps = new List<string>
                    {
                        "1. ไปที่ Settings > Developer Options > เปิด OEM Unlocking",
                        "2. เปิด Command Prompt และพิมพ์: adb reboot bootloader",
                        "3. พิมพ์: fastboot oem get_unlock_data",
                        "4. คัดลอกข้อมูลทั้งหมดที่แสดง (5 บรรทัด) และรวมเป็น string เดียว",
                        "5. ไปที่เว็บ Motorola Bootloader Unlock และล็อกอิน",
                        "6. วางข้อมูลและขอรหัส Unlock",
                        "7. รับรหัสทางอีเมลและพิมพ์: fastboot oem unlock [รหัส]"
                    },
                    Warnings = new List<string>
                    {
                        "ข้อมูลทั้งหมดจะถูกลบ!",
                        "เครื่องจาก Carrier บางรายอาจ Unlock ไม่ได้",
                        "ประกันจะถูกยกเลิก"
                    }
                },
                new BootloaderGuide
                {
                    Brand = "Huawei / Honor (เก่า)",
                    Icon = "📱",
                    Difficulty = "Hard",
                    WaitTime = "ไม่รองรับอีกแล้ว",
                    OfficialUrl = "",
                    Requirements = new List<string>
                    {
                        "Huawei หยุดให้บริการ Unlock Code ตั้งแต่ปี 2018",
                        "ต้องใช้บริการ Paid Unlock จากเว็บ Third-party"
                    },
                    Steps = new List<string>
                    {
                        "1. Huawei ไม่ให้บริการ Unlock Code อีกแล้ว",
                        "2. ต้องใช้บริการ DC-Unlocker หรือ HCU Client (มีค่าใช้จ่าย)",
                        "3. หรือใช้ PotatoNV สำหรับบาง Kirin SoC",
                        "4. ค้นหา 'Huawei bootloader unlock [รุ่นเครื่อง]' สำหรับวิธีเฉพาะรุ่น"
                    },
                    Warnings = new List<string>
                    {
                        "ระวังเว็บหลอกลวง!",
                        "มีค่าใช้จ่าย $4-50 ขึ้นอยู่กับบริการ",
                        "บางรุ่นอาจ Unlock ไม่ได้เลย"
                    }
                },
                new BootloaderGuide
                {
                    Brand = "OPPO / Realme / Vivo",
                    Icon = "📱",
                    Difficulty = "Hard",
                    WaitTime = "แตกต่างกันไป",
                    OfficialUrl = "",
                    Requirements = new List<string>
                    {
                        "OPPO: ต้องสมัคร Developer Account และได้รับอนุมัติ",
                        "Realme: ใช้ Deep Testing App",
                        "Vivo: ส่วนใหญ่ไม่รองรับ"
                    },
                    Steps = new List<string>
                    {
                        "สำหรับ Realme:",
                        "1. ดาวน์โหลด Deep Testing APK จากเว็บ Realme",
                        "2. ติดตั้งและเปิดแอพ",
                        "3. สมัครและรอการอนุมัติ (1-7 วัน)",
                        "4. เมื่อได้รับอนุมัติ กด Apply for Unlock",
                        "5. Reboot เข้า Fastboot และ Unlock ด้วย fastboot flashing unlock",
                        "",
                        "สำหรับ OPPO:",
                        "1. สมัคร OPPO Developer Account",
                        "2. รอการอนุมัติ (หลายสัปดาห์)",
                        "3. ใช้ OPPO Unlock Tool"
                    },
                    Warnings = new List<string>
                    {
                        "กระบวนการซับซ้อนและใช้เวลานาน",
                        "บางรุ่นไม่รองรับเลย",
                        "Vivo ส่วนใหญ่ Unlock ไม่ได้"
                    }
                }
            };
        }

        public static List<FrpBypassGuide> GetFrpGuides()
        {
            return new List<FrpBypassGuide>
            {
                new FrpBypassGuide
                {
                    Brand = "Samsung",
                    Method = "Combination Firmware",
                    AndroidVersions = "All",
                    Difficulty = "Medium",
                    Tools = new List<string> { "Odin", "Combination Firmware" },
                    Steps = new List<string>
                    {
                        "1. ดาวน์โหลด Combination Firmware สำหรับรุ่นและ CSC ที่ตรงกัน",
                        "2. เข้า Download Mode (Vol Down + Vol Up + Power)",
                        "3. เปิด Odin และ Flash Combination",
                        "4. บูทเข้าระบบและลบ Google Account จาก Settings",
                        "5. Flash Stock Firmware กลับ"
                    },
                    Warning = "ใช้เฉพาะเครื่องที่คุณเป็นเจ้าของจริงเท่านั้น!"
                },
                new FrpBypassGuide
                {
                    Brand = "Xiaomi",
                    Method = "Mi Account Removal",
                    AndroidVersions = "MIUI 10-14",
                    Difficulty = "Hard",
                    Tools = new List<string> { "Mi Flash Pro", "MIUI Debloater" },
                    Steps = new List<string>
                    {
                        "1. เข้า Recovery Mode (Vol Up + Power)",
                        "2. เลือก Wipe Data",
                        "3. ถ้ายังติด FRP ต้อง Flash ROM ใหม่ผ่าน EDL Mode",
                        "4. ใช้ Mi Flash Pro เพื่อ Flash ROM ที่ไม่มี FRP"
                    },
                    Warning = "EDL Mode ต้องใช้ Authorized Account บางกรณี"
                },
                new FrpBypassGuide
                {
                    Brand = "Universal (Android 11+)",
                    Method = "ADB Method",
                    AndroidVersions = "Android 11-14",
                    Difficulty = "Easy",
                    Tools = new List<string> { "ADB", "OTG Cable", "USB Keyboard" },
                    Steps = new List<string>
                    {
                        "1. เชื่อมต่อ USB Keyboard ผ่าน OTG",
                        "2. เปิด TalkBack ด้วย 3 นิ้วค้าง",
                        "3. วาด L บนหน้าจอเพื่อเปิด Context Menu",
                        "4. ไปที่ TalkBack Settings > Help > Open in Browser",
                        "5. ดาวน์โหลด APK ที่ช่วย Bypass (เช่น FRP Bypass APK)",
                        "6. ติดตั้งและทำตามขั้นตอน"
                    },
                    Warning = "วิธีนี้อาจไม่ทำงานกับ Security Patch ใหม่"
                }
            };
        }
    }
}
