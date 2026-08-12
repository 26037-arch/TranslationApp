# TranslationApp 구현 보고서

## 현재 구조 분석

기존 앱은 `HotkeyManager → SelectionReader/OCR → TranslationWorkflowService → TranslationRequestQueue → NllbTranslator/Python worker` 순서였습니다. 번역이 끝난 뒤에만 `TranslationWindow`가 생성됐고, `SemaphoreSlim`은 동시 실행을 막았지만 엄격한 FIFO 작업 항목과 요청별 결과 대상을 명시적으로 보존하지 않았습니다.

언어 선택 컨트롤은 없으며 `Ctrl+Alt+E/K`가 목표 언어를 결정합니다. 기존 NLLB 매핑도 목표가 영어면 출발을 한국어, 목표가 한국어면 출발을 영어로 정했습니다. 이 규칙을 Google 언어 코드(`ko`, `en`)로 그대로 보존했습니다.

Python/NLLB 의존성은 `App`, `AppSettings`, 설정창, 트레이 상태, csproj 콘텐츠, `setup-runtime.ps1`, `Runtime`, 번역 서비스와 테스트에 연결되어 있었습니다. Google 방식 연결 후 참조를 다시 검색해 공통 OCR/Tesseract 코드와 무관함을 확인한 뒤 제거했습니다.

## 변경된 번역 아키텍처

```text
현재 클립보드 텍스트 또는 OCR 선택
  → TranslationRequest 생성(RequestId + 원문 + sl/tl snapshot)
  → TranslationWindow 즉시 표시("번역 중…")
  → Channel<T> FIFO Queue
  → GoogleTranslateService
  → 앱 전체 단일 GoogleTranslateHostWindow/WebView2
  → URL Navigate
  → NavigationCompleted
  → DOM polling
  → ViewModel 결과 적용
```

`TranslationWindowPresenter`, `WindowRegistry`, UI Automation 선택 위치, 겹침 회피, OCR 문서 세그먼트와 원문 복원 흐름은 유지했습니다.

## 추가 파일

- `Services/Translation/GoogleTranslateLanguageCodes.cs`
- `Services/Translation/GoogleTranslateUrlBuilder.cs`
- `Services/Translation/GoogleTranslateService.cs`
- `Services/Translation/GoogleTranslateDomExtractor.cs`
- `Services/Translation/IGoogleTranslateWebClient.cs`
- `Services/Translation/ITranslationEngineLifecycle.cs`
- `Services/Translation/WebView2RuntimeInstaller.cs`
- `Views/GoogleTranslateHostWindow.xaml(.cs)`
- `TranslationApp.Tests/GoogleTranslateTests.cs`
- `TranslationApp.Tests/GoogleTranslateLiveTests.cs`

## 주요 수정 파일

- `App.xaml.cs`: 단일 WebView2 호스트 생성, 엔진 선초기화, 종료 정리
- `TranslationWorkflowService.cs`: 창을 먼저 표시하고 비동기로 큐 요청
- `TranslationRequestQueue.cs`: `Channel<T>` 단일 소비자 FIFO processor
- `TranslationViewModel.cs`: loading/success/failure 상태, 창 닫힘 취소, stale RequestId 방어
- `TranslationRequest.cs`, `TranslationResult.cs`, `TranslationSession.cs`: 출발/도착 언어와 RequestId 보존
- `TrayIconService.cs`: 번역 엔진 상태 표시
- `AppSettings.cs`, `SettingsWindow.xaml`: Python/NLLB 설정 제거
- `TranslationApp.csproj`: `Microsoft.Web.WebView2` 안정 버전 참조
- `README.md`: 현재 요구사항·개인정보·제약으로 갱신

## 제거 파일

- `Services/Translation/NllbTranslator.cs`
- `Services/Translation/NllbLanguageCodes.cs`
- `Services/Translation/DummyTranslator.cs`
- `Services/Translation/IModelLifecycle.cs`
- `Models/TranslationOptions.cs`
- `Runtime/nllb_worker.py`
- `Runtime/requirements.txt`
- `setup-runtime.ps1`
- `TranslationApp.Tests/NllbIpcTests.cs`

Python interpreter 탐색, PyTorch, Hugging Face 모델 다운로드, INT8 양자화, JSON worker IPC, 모델 경로/thread 설정과 모델 상태 메시지는 더 이상 앱에 포함되지 않습니다.

## Google Translate URL과 입력 검증

`GoogleTranslateUrlBuilder`가 `https://translate.google.com/?sl=...&tl=...&text=...&op=translate`를 생성하며 `Uri.EscapeDataString`으로 원문을 인코딩합니다. 요청 생성 순간 언어 enum 값을 복사하므로 이후 UI/설정 변화가 이미 대기 중인 요청에 영향을 주지 않습니다.

빈 입력, 정의되지 않은 언어 enum, 같은 출발/도착 언어, 5,000자 초과 입력, 60,000자 초과 URI는 자르지 않고 거부합니다.

## FIFO와 수명주기

`Channel.CreateUnbounded`를 `SingleReader=true`로 구성했습니다. 요청별 `TaskCompletionSource`는 ViewModel이나 Window를 직접 참조하지 않습니다. 한 요청의 예외는 해당 completion에만 전달되며 processor는 다음 항목을 계속 읽습니다.

창이 닫히면 ViewModel CTS가 취소됩니다. 대기 요청은 dequeue 전에 건너뛰고, 실행 중 요청은 WebView polling까지 연동 취소됩니다. 앱 종료 시 hotkey 접수를 중단하고 queue writer 완료, processor 취소, navigation generation 증가, WebView2 dispose와 host close 순서로 정리합니다.

## WebView2 호스트와 Runtime

호스트는 1024×768 데스크톱 뷰포트를 화면 밖 WPF 창으로 유지하며 `ShowInTaskbar=false`, `ShowActivated=false`, `WS_EX_NOACTIVATE`를 사용합니다. `Collapsed` 상태가 아니므로 CoreWebView2 초기화와 반응형 결과 DOM 렌더링이 가능합니다.

`CoreWebView2Environment.GetAvailableBrowserVersionString`으로 Evergreen Runtime을 확인합니다. 없으면 Microsoft 공식 HTTPS 링크 `https://go.microsoft.com/fwlink/p/?LinkId=2124703`에서 bootstrapper를 임시 파일로 받고 최종 redirect host가 Microsoft 도메인인지 검사한 뒤 `/silent /install`로 실행합니다. 설치 후 Runtime을 재검사하고 임시 파일은 성공/실패 모두 정리합니다.

WebView2는 `%LOCALAPPDATA%\TranslationApp\WebView2` user data folder를 사용합니다. 컨텍스트 메뉴, DevTools, 상태 표시줄, zoom UI, accelerator key, swipe, 새 창, 다운로드와 권한 요청은 비활성화했습니다. 최상위 이동은 HTTPS `translate.google.com`만 허용합니다.

## DOM selector와 polling

selector는 `GoogleTranslateDomExtractor.TranslationResultSelectors` 한 곳에 둡니다. 현재 live 페이지에서 확인한 결과 구조는 다음과 같습니다.

```text
c-wiz[role="region"][data-node-index]
  span[jsname="jqKxS"]
    span[jsname="txFAF"]
      span[jsname="W297wb"]
```

실제 live 검증에서 `c-wiz[role="region"][data-node-index] span[jsname="W297wb"]`가 `안녕하세요 → Hello` 결과를 반환했습니다. 이 구조 후보 외에도 `data-result-index`, `aria-live`, `data-language-for-alternatives`, `lang`, `jsname` 조합을 순서대로 검사합니다.

메뉴·버튼·대화상자·목록 영역을 제외하고, 보이는 노드와 목표 `lang`만 허용하며, 정규화한 결과가 원문과 같으면 거부합니다. `ExecuteScriptAsync` JSON은 `System.Text.Json`으로 객체 역직렬화하여 따옴표·줄바꿈·Unicode를 보존합니다.

Navigation 완료 후 즉시 한 번 검사하고 `PeriodicTimer`로 200ms 간격 polling합니다. 한 시도는 외부 CTS의 15초 제한을 받습니다.

## retry와 경쟁 상태 방지

각 요청은 최대 두 번 시도합니다. timeout, navigation 오류, JavaScript 오류 또는 selector 실패 뒤 첫 시도만 reload/navigate하여 재시도합니다. 두 번째 실패는 해당 요청에만 최종 오류를 반환합니다.

각 navigation에는 증가하는 generation과 WebView2 navigation ID를 함께 사용합니다. 이전 요청의 늦은 `NavigationCompleted`는 ID가 다르고, 이전 `ExecuteScriptAsync` 결과는 generation이 다르므로 적용되지 않습니다. Queue 자체도 WebView2 호출을 하나씩만 실행합니다.

## 로그와 예외 처리

enqueue/dequeue, RequestId, 입력 길이, 언어, navigation 시작/완료, generation, attempt, selector 성공, timeout, retry와 최종 실패를 기록합니다. 번역 원문 전체는 기록하지 않습니다.

인터넷/DNS/Google 페이지, Runtime 탐지·설치, CoreWebView2 초기화, navigation, DOM/JavaScript, timeout, 취소와 앱 종료 오류는 요청 또는 초기화 경계에서 처리해 앱 프로세스와 queue processor가 종료되지 않도록 했습니다.

## 검증

- NuGet restore 성공
- Release build: warning 0, error 0
- 기존·신규 전체 테스트: 34개 통과
- 실제 Google Translate WebView2 live test: 통과
- 실제 DOM에서 의미 기반 selector 성공 확인
- 현재 클립보드 텍스트 읽기와 클립보드 잠금 재시도 테스트 통과
- 자체 포함 게시본 `--smoke-test`: 종료 코드 0

`GoogleTranslateLiveTests`는 기본 실행에서는 외부 웹 호출을 생략하며 `TRANSLATIONAPP_RUN_WEB_TESTS=1`일 때 실제 페이지를 검사합니다.

## 남은 수동 확인

1. 텍스트 및 이미지가 담긴 클립보드에서 각각 `Ctrl+Alt+E/K`
2. 창이 즉시 `번역 중…`으로 열리고 외부 앱 포커스가 유지되는지
3. 연속 요청 A/B/C가 순서대로 각 창에 들어가는지
4. 대기/처리 중 창을 닫아도 다음 요청이 처리되는지
5. OCR 선택 번역 후 문서 치환·직접 편집·원문 복원·Undo/Redo
6. 인터넷 차단 및 Google 접속 실패 시 두 번 뒤 오류 UI와 다음 요청 처리
7. WebView2 Runtime이 없는 테스트 PC에서 공식 bootstrapper 자동 설치
8. Google Translate DOM 변경 시 중앙 selector 목록 유지보수
