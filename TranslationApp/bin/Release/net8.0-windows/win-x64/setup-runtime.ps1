param(
    [string]$PythonExecutable = "python",
    [string]$ModelDirectory = "$env:LOCALAPPDATA\TranslationApp\models\nllb-200-distilled-600M"
)

$ErrorActionPreference = "Stop"
$requirements = "$PSScriptRoot\TranslationApp\Runtime\requirements.txt"
if (-not (Test-Path -LiteralPath $requirements)) {
    $requirements = "$PSScriptRoot\Runtime\requirements.txt"
}
& $PythonExecutable -c "import torch; print('PyTorch', torch.__version__)"
if ($LASTEXITCODE -ne 0) {
    & $PythonExecutable -m pip install torch --index-url https://download.pytorch.org/whl/cpu
}
& $PythonExecutable -m pip install -r $requirements
& $PythonExecutable -c @"
from huggingface_hub import snapshot_download
snapshot_download(
    repo_id='facebook/nllb-200-distilled-600M',
    local_dir=r'$ModelDirectory',
    allow_patterns=['*.json', '*.model', '*.txt', '*.safetensors', '*.bin'],
)
print(r'Model ready: $ModelDirectory')
"@
