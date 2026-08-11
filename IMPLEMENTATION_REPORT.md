# TranslationApp 구현 보고서

## 사전 점검과 결정

- 기존 작업 폴더는 비어 있어 보존할 코드가 없었습니다.
- .NET SDK가 시스템에 없어서 검증용 휴대형 .NET 8.0.423 SDK를 작업 임시 폴더에만 설치했습니다.
- `C:\Program Files\Tesseract-OCR\tesseract.exe` 5.5 및 `kor.traineddata`, `eng.traineddata`를 확인했습니다.
- C#에서 NLLB seq2seq 생성기를 일부만 재구현하지 않고, 장기 실행 Python/PyTorch worker를 선택했습니다. Worker는 모델을 한 번 로드하고 `Linear` 계층을 dynamic INT8로 양자화한 뒤 stdin/stdout JSON IPC로만 통신합니다. 번역 텍스트를 네트워크로 보내지 않으며 worker 환경은 Hugging Face/Transformers 오프라인 모드로 강제됩니다.

## Phase 1 — 기본 앱과 외부 선택

변경 파일: `Models`, `Infrastructure`, `Services/Hotkeys`, `Services/Clipboard`, `TranslationWindow` 및 ViewModel.

구현 내용:

- `RegisterHotKey` 기반 `Ctrl+Alt+E/K/O` 전역 단축키
- 외부 앱의 현재 선택에 `SendInput`으로 `Ctrl+C` 전달
- 원래 `IDataObject`의 여러 포맷을 복제하고 retry/backoff로 즉시 복원
- 클립보드 시퀀스 번호 기반 갱신 대기(고정 sleep 없음)
- 요청별 독립 번역 창, 선택 가능한 결과 TextBox, 전체 복사, 대안 번역 UI

자동 검증: 클립보드 텍스트와 사용자 포맷이 함께 복원되는 단위 테스트, `CLIPBRD_E_CANT_OPEN` 잠금 재시도, x64 Win32 `INPUT` ABI 및 실제 Ctrl/C down/up 입력 주입 테스트.

수동 확인: Chrome/Edge/Firefox/VS Code/Word/PDF Viewer에서 실제 선택 복사는 각 앱 및 보안 수준에 따라 확인이 필요합니다.

## Phase 2 — NLLB 로컬 추론

변경 파일: `NllbTranslator`, `TranslationRequestQueue`, `NllbLanguageCodes`, `Runtime/nllb_worker.py`, `requirements.txt`, `setup-runtime.ps1`.

구현 내용:

- `facebook/nllb-200-distilled-600M`, `kor_Hang`/`eng_Latn` 명시 매핑
- CPU thread 제한 및 PyTorch dynamic INT8
- 시작 시 한 번 로드하고 프로세스 종료까지 모델 상주
- 줄 단위 JSON IPC, stdout 프로토콜과 stderr 로그 분리
- 직렬 요청 큐, 대기 취소, 생성 취소 시 worker 종료/다음 요청 재시작
- worker 비정상 종료 시 한 번 자동 재시작, 상태 및 사용자 메시지 제공
- beam search로 요청 시에만 최대 4개 대안 생성 및 중복 제거

자동 검증: 언어 매핑, 직렬 동시성, 대기 취소, Python 문법, 설치된 PyTorch의 실제 dynamic INT8 변환, BOM 없는 stdin/stdout, 한글/영문/특수문자 JSON, 최초 요청과 worker 재시작 후 요청, 실제 NLLB 600M 첫 번역.

제약: 실제 600M 모델 파일은 산출물에 포함하지 않습니다. `setup-runtime.ps1`로 한 번 다운로드해야 하며 실제 문장 품질/속도는 모델 설치 후 수동 검증이 필요합니다.

## Phase 3 — 화면 OCR

변경 파일: `Services/Ocr`, `OcrSelectionOverlay`, `OcrSourceWindow`, `OcrSourceViewModel`.

구현 내용:

- 현재 커서 모니터를 오버레이 표시 전에 캡처
- PerMonitorV2 DPI에서 WPF 선택 좌표를 캡처 픽셀로 변환
- 드래그 crop, ESC 취소, 5px 미만 영역 거부
- 외부 프로세스 Tesseract `kor+eng`, `--psm 6`, 임시 PNG 정리
- 여러 개를 동시에 열 수 있는 일반 TextBox 기반 OCR 편집 창
- WPF 기본 편집/복사/붙여넣기/삭제/Undo/Redo 및 `Ctrl+Shift+Z` Redo

자동 검증: 빈/공백 OCR 결과 거부 및 줄바꿈 정규화.

수동 확인: 서로 다른 배율의 실제 다중 모니터에서 crop 정합성과 이미지 종류별 OCR 품질 확인이 필요합니다.

## Phase 4 — 세그먼트와 문서 연동

변경 파일: `TranslationSegment`, `OcrDocumentController`, `SegmentAnchorTracker`, `TranslationViewModel`.

구현 내용:

- Original/Current text, 후보 이력, target, 생성 범위, 현재 anchor, 연결 상태 보존
- 앞쪽 편집 시 anchor 이동, 내부 편집 시 범위와 CurrentText 갱신
- 전체 삭제만 Detached; 전체를 다른 텍스트로 교체하면 연결 유지
- Primary/대안/번역 창 수동 편집/원문 복원을 동일 문서 범위에 적용
- WPF 편집 작업으로 적용하여 OCR 편집기의 Undo/Redo에 포함
- Detached 창은 유지하며 확인/대안/복사는 가능하고 문서 쓰기/복원만 금지

자동 검증: 앞쪽 편집, 내부 편집, 전체 교체, 전체 삭제, 수동 결과 편집 상태.

## Phase 5 — 위치와 포커스

변경 파일: `SelectionBoundsService`, `WindowPlacementCalculator`, `TranslationWindowPresenter`.

구현 내용:

- UI Automation `TextPattern.Selection` bounding rectangles 우선 사용
- 실패 시 현재 커서 위치 폴백
- monitor work area clamp, 모니터 DPI를 반영한 물리 창 크기
- anchor 아래/오른쪽/위/왼쪽 후보와 기존 번역 창 45% 이상 겹침 회피
- `ShowActivated=false`와 `SWP_NOACTIVATE`로 외부 앱 포커스 유지; 사용자가 클릭하면 정상 활성화

자동 검증: 작업 영역 clamp와 기존 창 심한 겹침 회피.

수동 확인: 각 대상 앱의 UI Automation 지원 정도와 선택 영역 근처 초기 배치를 확인해야 합니다.

## Phase 6 — 수명주기와 품질

변경 파일: `App.xaml.cs`, tray/settings/startup/logging 서비스, xUnit 테스트 프로젝트, README.

구현 내용:

- 트레이 상주, 모델 상태 표시, OCR/설정/종료 메뉴
- Tesseract/Python/model/thread 설정 JSON
- 제거 가능한 HKCU Run 시작 프로그램 등록
- `%LOCALAPPDATA%\TranslationApp\logs` 개발 로그와 비모달 사용자 알림
- 모델/의존성 부재 시 크래시 없이 Failed 상태 유지
- 자체 포함 `win-x64` 배포 구성

검증 결과: Release 빌드 경고 0/오류 0, xUnit 24개 통과, 실제 NLLB 모델 통합 테스트 통과, 모델 미설치 상태 앱 smoke test에서 프로세스 정상 유지 및 진단 로그 확인.

## 실제 로그 기반 안정화 수정

- `NllbTranslator`의 redirected stdin/stdout/stderr를 `new UTF8Encoding(false)`로 고정해 프로세스 시작과 재시작 모두 BOM을 내보내지 않습니다.
- Python worker는 stdout JSON을 UTF-8 bytes로 직접 기록하며 첫 stdin line의 UTF-8 BOM을 방어적으로 제거합니다.
- `INPUT` union에 `MOUSEINPUT`, `KEYBDINPUT`, `HARDWAREINPUT`을 모두 선언해 x64 크기를 네이티브 요구값 40 bytes로 맞췄습니다. 기존 32 bytes 선언이 Win32 오류 87의 원인이었습니다.
- Clipboard backup/read/restore 전 구간에 최대 9회, 약 2.1초의 비동기 지수 backoff를 적용했습니다.

## 수동 종합 시나리오

1. `setup-runtime.ps1` 실행 후 앱을 시작하고 트레이 상태가 준비됨으로 바뀌는지 확인합니다.
2. Chrome에서 텍스트를 선택하고 `Ctrl+Alt+K`; Chrome 포커스 유지, 원래 클립보드 유지, 선택 근처 번역 창을 확인합니다.
3. 결과 선택/`Ctrl+C`/전체 복사/다른 번역 보기를 확인합니다.
4. `Ctrl+Alt+O`로 화면을 선택하고 OCR 편집 창에서 일부 텍스트를 선택해 번역합니다.
5. Primary 즉시 치환, 대안 선택 재치환, 번역 직접 편집, 원문 복원, Undo/Redo를 확인합니다.
6. 세그먼트를 완전히 삭제한 뒤 번역 창이 Detached 상태로 유지되는지 확인합니다.
