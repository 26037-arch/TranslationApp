# TranslationApp

Windows 11용 WPF/.NET 8 로컬 한국어↔영어 번역 유틸리티입니다. 선택 텍스트와 Tesseract 화면 OCR을 지원하며 번역 내용은 외부 번역 API로 전송하지 않습니다.

## 단축키

- `Ctrl+Alt+E`: 현재 선택을 영어로 번역
- `Ctrl+Alt+K`: 현재 선택을 한국어로 번역
- `Ctrl+Alt+O`: 커서가 있는 모니터에서 OCR 영역 선택

외부 앱 선택은 클립보드를 전체 데이터 개체로 백업하고 `Ctrl+C` 결과를 읽은 직후 복원합니다. OCR 편집 창 안에서는 클립보드를 거치지 않고 WPF 선택 API를 사용합니다.

## 준비 및 실행

1. Windows x64용 .NET 8 SDK가 필요합니다.
2. 저장소 루트에서 `./setup-runtime.ps1`을 실행합니다. 이 단계만 Python 패키지와 NLLB 모델을 다운로드합니다. 이후 앱의 번역 추론은 오프라인입니다.
3. `dotnet build TranslationApp.sln -c Release`를 실행합니다.
4. `TranslationApp/bin/Release/net8.0-windows/TranslationApp.exe`를 실행합니다.

준비 스크립트는 PyTorch가 없을 때만 CPU 빌드를 설치합니다. 모델은 기본적으로 `%LOCALAPPDATA%\TranslationApp\models\nllb-200-distilled-600M`에 저장됩니다. 모델 폴더, Python, Tesseract 및 tessdata 경로는 트레이 메뉴의 **설정**에서 변경할 수 있습니다. 기본 Tesseract 경로는 `C:\Program Files\Tesseract-OCR`이고 OCR 언어는 `kor+eng`입니다.

## 로컬 추론 구조

WPF 프로세스는 시작 시 장기 실행 Python worker 하나를 시작합니다. worker는 `facebook/nllb-200-distilled-600M`을 CPU에 한 번 로드하고 `Linear` 계층에 PyTorch dynamic INT8 양자화를 적용합니다. 요청과 응답은 BOM 없는 UTF-8의 줄 단위 JSON으로만 교환되고, 로그는 stderr로 분리됩니다. Worker는 이전 버전 클라이언트를 위해 첫 입력의 BOM도 방어적으로 제거합니다. C#의 직렬 큐가 동시 추론으로 인한 CPU 과부하와 결과 혼선을 막습니다. 취소로 생성 중인 Python 호출을 즉시 중단해야 할 때는 worker를 종료하고 다음 요청에서 다시 시작합니다.

## 데이터와 로그

- 설정: `%LOCALAPPDATA%\TranslationApp\settings.json`
- 로그: `%LOCALAPPDATA%\TranslationApp\logs\translationapp-YYYYMMDD.log`
- 시작 프로그램: 현재 사용자 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`의 `TranslationApp` 값

## 알려진 제약

- NLLB 600M 모델 최초 준비에는 인터넷과 수 GB의 디스크 공간이 필요합니다. 번역 시에는 네트워크가 강제로 비활성화됩니다.
- 동적 INT8은 행렬 곱 계층을 양자화하며 embedding 등 일부 계층은 부동소수점으로 남습니다.
- UI Automation을 제공하지 않는 앱에서는 번역 창 위치가 현재 마우스 커서로 폴백합니다.
- 관리자 권한으로 실행된 앱의 선택에는 Windows 무결성 수준 때문에 일반 권한 TranslationApp이 `Ctrl+C`를 보낼 수 없습니다.
