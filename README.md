# VIVE Focus Vision 眼動資料擷取系統（Unity）

本專案是一個以 **HTC VIVE Focus Vision** 為目標裝置的 Unity 眼動追蹤研究原型，使用 **VIVE OpenXR Plugin** 取得眼動資料，並將資料記錄為 CSV，供 HCI、VR 使用者研究與後續 AOI／fixation 分析使用。

本 README 以「**非原開發者也能在另一台電腦重新開啟、建置與測試**」為目標撰寫。

---

## 目前功能

- 取得左右眼 gaze origin 與 gaze direction
- 記錄左右眼 gaze validity
- 計算 derived combined gaze
- 顯示 gaze ray 與命中點
- 偵測目前注視的 Unity GameObject
- 取得左右眼 pupil diameter
- 取得左右眼 eye openness
- 記錄頭部位置與旋轉
- 以固定頻率輸出研究用 CSV
- CSV 儲存在 Focus Vision 的 App 專屬資料夾
- 可透過 Android Debug Bridge（ADB）將資料複製到電腦

---

# 一、相容性

## 已實際驗證的環境

| 項目 | 本專案環境 |
|---|---|
| 作業系統 | Windows 10／11 |
| Unity | `6000.4.4f1` |
| Build Target | Android |
| CPU Architecture | ARM64 |
| XR Runtime | OpenXR |
| VIVE SDK | VIVE OpenXR Plugin |
| VIVE OpenXR Plugin | 建議 `2.5.1`；至少 `2.5.0` |
| 目標裝置 | HTC VIVE Focus Vision |
| 執行模式 | Focus Vision AIO／Standalone Android |
| 眼動功能 | VIVE XR Eye Tracker |
| 資料格式 | UTF-8 CSV |

> 建議在其他電腦上使用與本專案完全相同的 Unity 版本。  
> 請先查看 `ProjectSettings/ProjectVersion.txt`，不要在第一次開啟時自行升級 Unity。

## 裝置相容狀態

| 裝置與模式 | 相容情況 | 備註 |
|---|---:|---|
| VIVE Focus Vision AIO | ✅ 已驗證 | 本專案主要使用方式 |
| VIVE Focus Vision PC Streaming | 🟡 官方支援，但本專案未完整驗證 | 可搭配 Direct Preview 或 Streaming |
| VIVE XR Elite AIO | 🟡 官方支援 Advanced Eye Tracking | 仍需實機重新驗證 |
| VIVE Focus 3 AIO | ❌ 不支援本專案使用的 Advanced Eye Tracking | 不應直接視為相容 |
| 一般 Unity Editor Play | ❌ 無法代表 Focus Vision AIO | 沒有正確 PC Runtime 時通常讀不到真實眼動 |
| Android 手機 | ❌ 不相容 | 缺少 VIVE OpenXR Runtime 與眼動硬體 |

VIVE 官方資料顯示，Focus Vision AIO 的 Advanced Eye Tracking 需要 **VIVE OpenXR Plugin 2.5.0 以上**。本專案使用的左右眼 gaze、pupil 與 eye openness 都依賴 `VIVE XR Eye Tracker`。

---

# 二、開始前需要準備的東西

## 硬體

- HTC VIVE Focus Vision
- 支援資料傳輸的 USB-C 線
- Windows 電腦
- 可用的 USB 連接埠

## 軟體

1. Git
2. Unity Hub
3. Unity `6000.4.4f1`
4. Unity Android Build Support
5. Android SDK & NDK Tools
6. OpenJDK

安裝 Unity Android 模組時，Unity Hub 中應勾選：

```text
Android Build Support
├── Android SDK & NDK Tools
└── OpenJDK
```

不需要另外安裝 Android Studio。

---

# 三、將專案移植到另一台電腦

## Step 1：下載專案

使用 Git：

```powershell
git clone <此 GitHub Repository 的 HTTPS URL>
cd <專案資料夾名稱>
```

也可以在 GitHub 頁面選擇：

```text
Code → Download ZIP
```

下載 ZIP 後請完整解壓縮，不要直接在壓縮檔內開啟 Unity 專案。

---

## Step 2：確認專案結構

專案根目錄至少應包含：

```text
Assets/
Packages/
ProjectSettings/
.gitignore
README.md
```

以下資料夾沒有上傳是正常的，Unity 會重新產生：

```text
Library/
Temp/
Logs/
Obj/
UserSettings/
Builds/
```

---

## Step 3：安裝正確 Unity 版本

開啟：

```text
ProjectSettings/ProjectVersion.txt
```

確認其中版本。此專案目前為：

```text
6000.4.4f1
```

在 Unity Hub 中：

```text
Installs
→ Install Editor
→ Archive
→ 安裝 6000.4.4f1
```

安裝時記得加入 Android 三個模組。

---

## Step 4：從 Unity Hub 開啟專案

在 Unity Hub：

```text
Projects
→ Add
→ Add project from disk
```

選擇「包含 `Assets`、`Packages`、`ProjectSettings` 的專案根目錄」。

第一次開啟可能需要數分鐘重新建立 `Library`。期間不要強制關閉 Unity。

---

# 四、VIVE OpenXR Plugin 移植檢查

這是最容易造成其他電腦無法開啟專案的地方。

## 情況 A：Repo 已包含 Embedded Package

檢查是否存在：

```text
Packages/com.htc.upm.vive.openxr/package.json
```

若存在，Unity 應自動載入 VIVE OpenXR Plugin，不需額外安裝。

Package Manager 中通常會顯示：

```text
VIVE OpenXR Plugin
Source: Embedded
```

## 情況 B：Repo 沒有包含 VIVE Package

若 Unity Console 顯示：

```text
The package ... could not be found
VIVE.OpenXR namespace not found
```

請從 HTC VIVE Developer 官方網站下載 VIVE OpenXR Unity Plugin，然後：

```text
Window
→ Package Manager
→ 左上角 +
→ Add package from disk...
```

選擇：

```text
com.htc.upm.vive.openxr/package.json
```

### 重要：不要匯入 1 KB 的 `ViveOpenXRInstaller.unitypackage`

若下載到的 `.unitypackage` 只有約 1 KB，它通常只是 Git LFS pointer，不是完整 Unity Package。

此時應改用：

```text
VIVE-OpenXR-Unity/
└── com.htc.upm.vive.openxr/
    └── package.json
```

並透過 `Add package from disk...` 安裝。

## 檢查 `manifest.json` 是否仍指向舊電腦

開啟：

```text
Packages/manifest.json
```

若看到類似：

```json
"com.htc.upm.vive.openxr": "file:C:/Users/某人/Downloads/..."
```

代表它仍依賴原開發者電腦的本機路徑，在另一台電腦上會失效。

建議修正為 Embedded Package，或重新透過 Package Manager 指定新電腦上的 `package.json`。

---

# 五、Unity XR 設定檢查

開啟：

```text
Edit
→ Project Settings
→ XR Plug-in Management
```

切換到 Android 平台，確認：

```text
☑ OpenXR
```

接著進入：

```text
XR Plug-in Management
→ OpenXR
→ Android
```

確認下列設定。

## OpenXR Feature Group

```text
☑ VIVE XR Support
```

## Interaction Profiles

```text
VIVE Focus 3 Controller Interaction
Eye Gaze Interaction Profile
```

Focus Vision 使用 `VIVE Focus 3 Controller Interaction` 是正常的。

## Eye Tracking Feature

```text
☑ VIVE XR Eye Tracker (Beta)
```

功能用途：

| 設定 | 用途 |
|---|---|
| Eye Gaze Interaction Profile | OpenXR 標準 gaze pose |
| VIVE XR Eye Tracker | 左右眼 gaze、pupil、eye openness 等進階資料 |

最後開啟：

```text
Project Validation
→ Fix All
```

若仍有紅色 Error，先修正後再 Build。

---

# 六、Android Build 設定

開啟：

```text
File
→ Build Profiles
```

選擇：

```text
Android
→ Switch Platform
```

## Player Settings

前往：

```text
Edit
→ Project Settings
→ Player
→ Android
```

確認：

| 設定 | 建議值 |
|---|---|
| Default Orientation | Landscape Left |
| Scripting Backend | IL2CPP |
| Target Architectures | ARM64 |
| ARMv7 | 關閉 |
| Active Input Handling | Input System Package (New) |
| Graphics API | OpenGLES3 |
| Package Name | `com.hank.viveeyetracking` 或自訂唯一名稱 |

為避免初次移植的相容問題，建議先只保留：

```text
OpenGLES3
```

不要在尚未驗證前同時修改 Unity、OpenXR Plugin、Render Pipeline 與 Graphics API。

---

# 七、Focus Vision 裝置設定

## Step 1：開啟開發者模式

在 Focus Vision 中開啟：

```text
Settings
→ Developer options
→ USB debugging
```

## Step 2：連接電腦

1. 使用支援資料傳輸的 USB-C 線連接頭顯。
2. 戴上頭顯。
3. 接受「允許此電腦進行 USB 偵錯」。
4. 可勾選「一律允許」。

## Step 3：眼動校正

在 Focus Vision 系統設定中找到 Eye Tracking Calibration。

每位使用者開始測試前都應：

1. 正確戴好頭顯。
2. 調整頭帶與鏡片位置。
3. 完成眼動校正。
4. 回到 Unity App。
5. 確認左右眼 validity 大多為有效。

若更換使用者或頭顯位置明顯移動，應重新校正。

---

# 八、確認電腦是否找到 Focus Vision

本專案可使用 Unity 安裝的 ADB。

Unity `6000.4.4f1` 的預設位置通常為：

```powershell
$adb = "C:\Program Files\Unity\Hub\Editor\6000.4.4f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe"
```

確認檔案存在：

```powershell
Test-Path $adb
```

預期：

```text
True
```

確認裝置：

```powershell
& $adb devices
```

預期類似：

```text
List of devices attached
FA51J3N00242    device
```

若顯示 `unauthorized`，請戴上頭顯並接受 USB debugging 授權。

---

# 九、Build 到 Focus Vision

在 Unity：

```text
File
→ Build Profiles
→ Android
```

確認場景已加入 Scene List，然後選擇：

```text
Build and Run
```

第一次 Build 可能需要較長時間，因為 Unity 必須完成 IL2CPP 與 Android ARM64 編譯。

## 基礎成功標準

```text
[✓] APK 成功安裝並啟動
[✓] 頭部追蹤正常
[✓] 可以看到測試場景
[✓] 左右眼 gaze API 回傳成功
[✓] 左右眼大部分時間 valid=True
[✓] 頭部不動時，只移動眼睛仍會改變 gaze direction
[✓] 閉眼時對應眼睛可能變成 invalid
```

---

# 十、研究資料儲存位置

Focus Vision 是 Android 裝置，因此 App 無法直接寫入 Windows 的：

```text
C:\...
D:\...
```

CSV 會先存到頭顯的 App 專屬資料夾：

```text
/storage/emulated/0/Android/data/com.hank.viveeyetracking/files/EyeTrackingData/
```

實際路徑會受到 Package Name 影響。

錄製開始時，Android Logcat 會顯示類似：

```text
[EyeResearch] Recording started
/storage/emulated/0/Android/data/.../files/EyeTrackingData/TEST001_T01_GazeCSVTest_20260725_072454.csv
```

## 查看頭顯內的 CSV

```powershell
& $adb shell ls -lh /sdcard/Android/data/com.hank.viveeyetracking/files/EyeTrackingData
```

## 查看 CSV 行數

```powershell
& $adb shell wc -l /sdcard/Android/data/com.hank.viveeyetracking/files/EyeTrackingData/<檔名>.csv
```

## 複製資料到桌面

```powershell
cd $HOME\Desktop

New-Item -ItemType Directory -Path .\ViveEyeStudyData -Force

& $adb pull `
  /sdcard/Android/data/com.hank.viveeyetracking/files/EyeTrackingData `
  .\ViveEyeStudyData
```

完成後資料通常位於：

```text
Desktop/
└── ViveEyeStudyData/
    └── EyeTrackingData/
        └── *.csv
```

---

# 十一、CSV 主要欄位

| 類別 | 主要欄位 |
|---|---|
| 實驗識別 | participant_id、session_id、condition_id |
| 時間 | timestamp_utc、session_time_s、unity_frame、sample_index |
| 左眼 | left_valid、left_origin、left_direction |
| 右眼 | right_valid、right_origin、right_direction |
| 合併視線 | combined_valid、combined_origin、combined_direction |
| 瞳孔 | left/right_pupil_valid、pupil_diameter |
| 開眼程度 | left/right_openness_valid、openness |
| 頭部姿態 | head_position、head_rotation |
| 注視命中 | hit_valid、hit_object、hit_point |

`combined gaze` 是由左右眼資料計算出的 derived gaze，不應誤認為原始裝置欄位。

---

# 十二、資料安全與研究注意事項

- `participant_id` 應使用匿名代號，例如 `P001`。
- 不要在 CSV 中填寫真實姓名。
- 不要將受試者 CSV commit 到 GitHub。
- 專案 `.gitignore` 應排除：

```text
EyeTrackingData/
ViveEyeStudyData/
ResearchData/
*.csv
*.apk
*.aab
*.keystore
*.jks
```

- 正式研究前應完成受試者同意與資料保存規範。
- 頭顯內的 CSV 應在確認備份後再刪除。
- 眼動、瞳孔與注視物件資料可能具有敏感性，應限制存取權限。

---

# 十三、常見問題

## 1. Unity 顯示 `VIVE.OpenXR namespace not found`

原因通常是 VIVE OpenXR Plugin 沒有正確載入。

處理方式：

1. 檢查 `Packages/com.htc.upm.vive.openxr/package.json`。
2. 檢查 `Packages/manifest.json` 是否指向舊電腦路徑。
3. 重新使用 `Add package from disk...`。
4. 等待 Unity 完成重新編譯。

---

## 2. `GetEyeGazeData()` 一直回傳 false

檢查：

```text
Android OpenXR Loader 是否開啟
VIVE XR Support 是否勾選
VIVE XR Eye Tracker 是否勾選
VIVE OpenXR Plugin 是否至少為 2.5.0
是否在 Focus Vision 實機執行
是否完成眼動校正
```

---

## 3. API 成功，但 `left_valid`／`right_valid` 為 0

可能原因：

- 尚未校正
- 頭顯配戴位置不正確
- 眼睛被眼鏡、睫毛、瀏海或妝容遮擋
- 使用者正在閉眼
- App 剛啟動，追蹤尚未穩定
- 眼睛離鏡片過遠

請重新配戴並校正。

---

## 4. Unity Editor Play 中沒有眼動資料

一般 Editor Play 不等於 Focus Vision AIO。

可使用：

- Android Build and Run：最可靠
- VIVE Direct Preview：適合快速開發，但仍與 Android 原生渲染和效能不同

正式研究前仍應使用 Android AIO Build 驗證。

---

## 5. 離開 App 時沒有出現 `Recording saved`

Android 可能只把 App 放入背景，或直接終止 Process，因此不能只依賴 `OnApplicationQuit()`。

建議：

1. 在實驗流程中提供明確的 `End Session` 按鈕。
2. 按鈕呼叫 `EndSessionAndSave()` 或 `StopRecording()`。
3. 等待畫面或 Log 顯示儲存完成。
4. 再離開 App。
5. 同時保留定期 `Flush()` 作為備援。

即使沒有 `Recording saved` Log，也可用 `adb shell wc -l` 確認檔案是否已寫入。

---

## 6. Gaze Ray 一下偏左、一下偏右

這通常是因為左右眼 validity 短暫切換，使視覺射線起點在左右眼之間移動。

研究資料仍應保留原始左右眼資料；前端顯示則建議：

- 射線起點固定為 Main Camera 中央
- 左右眼只用於計算方向
- 視覺射線可加入輕微 smoothing
- 原始 CSV 不應套用視覺平滑

---

## 7. Ray 看得到，但沒有命中物件

檢查：

- 物件是否有 Collider
- Layer 是否包含在 Raycast LayerMask
- Max Distance 是否足夠
- Hit Marker 是否錯誤擋住射線
- 目標物件名稱是否正確

---

# 十四、建議的移植驗收流程

在新電腦上依序完成：

```text
[ ] 使用正確 Unity 版本開啟專案
[ ] Console 沒有紅色編譯錯誤
[ ] VIVE OpenXR Plugin 正常載入
[ ] Android OpenXR Loader 已啟用
[ ] VIVE XR Eye Tracker 已啟用
[ ] Focus Vision 可被 ADB 辨識
[ ] APK 可 Build and Run
[ ] 頭部追蹤正常
[ ] 左右眼 gaze validity 有效
[ ] gaze direction 會隨眼睛移動
[ ] CSV 成功建立
[ ] CSV 可透過 adb pull 複製到電腦
```

全部通過後，才視為移植完成。

---

# 十五、專案開發原則

本專案將功能拆成兩層：

## 前端互動層

負責：

- gaze ray 顯示
- 視覺平滑
- hit marker
- UI 狀態
- 使用者體驗

## 研究資料層

負責：

- 直接讀取 VIVE Eye Tracker API
- 保存未平滑的左右眼原始資料
- pupil 與 openness
- 頭部姿態
- gaze hit
- CSV

前端可調整射線顯示，但不可改變後端保存的原始研究資料。

---

# 十六、官方文件

- VIVE OpenXR Unity 最新版本與相容表  
  https://developer.vive.com/resources/openxr/unity/download/latest/

- VIVE XR Eye Tracker Unity 教學  
  https://developer.vive.com/resources/openxr/unity/tutorials/face-data/getting-the-data-of-eye-tracker/

- VIVE OpenXR Unity 基礎設定  
  https://developer.vive.com/resources/openxr/unity/tutorials/setup-and-installation/getting-started-with-openxr/

- VIVE OpenXR Direct Preview  
  https://developer.vive.com/resources/openxr/unity/tutorials/direct-preview/

- VIVE SDK 選擇說明  
  https://developer.vive.com/resources/getting-started-with-xr-elite/

---

## License

請依本 Repository 實際使用情況補上 License。

VIVE OpenXR Plugin、Unity Package 與第三方套件仍受各自授權條款約束。
