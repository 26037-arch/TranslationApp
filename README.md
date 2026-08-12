# TranslationApp

Windows용 WPF 한국어↔영어 번역 유틸리티입니다. 현재 클립보드의 텍스트와 Tesseract 화면 OCR을 지원하며, 번역은 앱 내부의 단일 WebView2에서 Google Translate 웹 UI를 열어 결과 DOM을 읽는 방식으로 처리합니다.

Google Cloud Translation API나 API 키를 사용하지 않습니다. Python, PyTorch, NLLB 모델 설치도 필요하지 않습니다.

## 단축키

- `Ctrl+Alt+E`: 현재 클립보드 텍스트를 영어로 번역
- `Ctrl+Alt+K`: 현재 클립보드 텍스트를 한국어로 번역
- `Ctrl+Alt+O`: 커서가 있는 모니터에서 OCR 영역 선택

한국어로 번역할 때 출발 언어는 영어, 영어로 번역할 때 출발 언어는 한국어로 요청 생성 시점에 고정됩니다. 일반 번역 단축키는 현재 클립보드의 텍스트를 변경하지 않고 읽습니다. 클립보드가 비어 있거나 텍스트가 아니면 우측 하단에 오류 알림을 표시합니다. OCR 편집 창 안에서는 WPF 선택 API를 사용합니다.

## 요구사항과 실행

- Windows 10/11 x64
- 인터넷 연결
- OCR 사용 시 Tesseract와 `kor`/`eng` 언어 데이터
- WebView2 Evergreen Runtime

WebView2 Runtime은 설치 여부를 앱 시작 시 확인합니다. 없으면 Microsoft 공식 HTTPS 배포 링크에서 Evergreen Bootstrapper를 임시 폴더로 내려받아 `/silent /install`로 자동 설치하고 임시 파일을 정리합니다. 자동 설치가 실패하면 앱은 종료되지 않고 사용자 알림과 로그를 남깁니다.

소스에서 빌드하려면 .NET 8 SDK가 필요합니다.

```powershell
dotnet restore TranslationApp.sln
dotnet build TranslationApp.sln -c Release
dotnet test TranslationApp.sln -c Release
```

실행 파일은 `TranslationApp/bin/Release/net8.0-windows/TranslationApp.exe`입니다. 자체 포함 게시본은 `publish/TranslationApp.exe`입니다.
초기화만 검증하고 정상 종료하는 스모크 모드는 `TranslationApp.exe --smoke-test`입니다.

## 번역 처리 구조

1. 현재 클립보드 텍스트 또는 OCR 선택으로 원문을 얻습니다.
2. 요청 ID, 원문, 출발 언어, 도착 언어를 `TranslationRequest`에 복사합니다.
3. 선택 영역 근처에 `TranslationWindow`를 즉시 열고 `번역 중…`을 표시합니다.
4. `Channel<T>` 기반 단일 소비자 FIFO 큐가 요청을 하나씩 처리합니다.
5. 앱 전체에서 하나뿐인 화면 밖 `GoogleTranslateHostWindow`와 WebView2를 재사용합니다.
6. URL의 `sl`, `tl`, `text`, `op=translate` query parameter로 안전하게 인코딩해 이동합니다.
7. Navigation 완료 뒤 200ms 간격으로 여러 DOM selector를 확인합니다.
8. 한 시도는 최대 15초이며 실패하면 한 번만 다시 Navigate/Reload합니다.
9. 성공 결과는 해당 ViewModel에만 전달되고, 실패한 요청 뒤에도 FIFO processor는 다음 요청을 계속 처리합니다.

입력은 최대 5,000 UTF-16 문자, 생성 URL은 최대 60,000자로 제한합니다. 제한을 넘으면 일부만 잘라 번역하지 않고 명확한 오류를 표시합니다.

## WebView2 호스트

호스트 창은 Google Translate의 반응형 결과 영역이 정상 렌더링되도록 `1024×768` 뷰포트를 유지하되 화면 밖에 배치하고, `ShowInTaskbar=false`, `ShowActivated=false`, `WS_EX_NOACTIVATE`로 생성합니다. `Visibility=Collapsed`를 사용하지 않으므로 CoreWebView2는 정상 초기화되지만 사용자 화면이나 작업 표시줄에는 나타나지 않습니다.

컨텍스트 메뉴, 개발자 도구, 상태 표시줄, 확대/축소 UI, 브라우저 단축키, 새 창, 다운로드와 권한 요청을 비활성화합니다. 상위 페이지 이동은 HTTPS `translate.google.com`만 허용합니다. WebView2 사용자 데이터는 `%LOCALAPPDATA%\TranslationApp\WebView2`에 저장됩니다.

## 데이터와 개인정보

- 설정: `%LOCALAPPDATA%\TranslationApp\settings.json`
- 로그: `%LOCALAPPDATA%\TranslationApp\logs\translationapp-YYYYMMDD.log`
- WebView2 데이터: `%LOCALAPPDATA%\TranslationApp\WebView2`
- 시작 프로그램: `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`의 `TranslationApp` 값

번역 원문은 Google Translate 웹페이지로 전송됩니다. 로그에는 원문 전체를 남기지 않고 요청 ID, 문자 수, 언어, 시도 횟수, selector와 오류만 기록합니다.

## 알려진 제한

- 이 구현은 Google의 공식 Cloud Translation API가 아니라 Google Translate 웹 UI의 DOM을 읽습니다. Google의 페이지 구조나 서비스 정책이 바뀌면 selector 유지보수가 필요할 수 있습니다.
- selector가 확실한 결과를 찾지 못하면 다른 페이지 문자열을 추측해 반환하지 않고 요청을 실패시킵니다.
- 인터넷, DNS, Google 페이지 또는 WebView2 Runtime 상태에 따라 번역이 실패할 수 있습니다.
- “다른 번역 보기”는 Google Translate에 새 요청을 보내며 같은 결과가 돌아올 수 있습니다.
- UI Automation을 제공하지 않는 앱에서는 번역 창 위치가 현재 마우스 커서로 폴백합니다.
- 다른 프로그램이 클립보드를 잠근 동안에는 잠시 재시도한 뒤 읽기 오류를 표시할 수 있습니다.

WebView2 공식 배포 문서: https://learn.microsoft.com/microsoft-edge/webview2/concepts/distribution
