"""Long-lived, offline NLLB worker. Stdout is reserved for newline-delimited JSON."""

import argparse
import json
import os
import sys
import time
import traceback


def emit(payload):
    # Write protocol bytes directly so stdout can never gain a text-encoding BOM.
    message = (json.dumps(payload, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
    sys.stdout.buffer.write(message)
    sys.stdout.buffer.flush()


def log(message):
    sys.stderr.write(str(message) + "\n")
    sys.stderr.flush()


def load_runtime(model_path, threads):
    os.environ["TRANSFORMERS_OFFLINE"] = "1"
    os.environ["HF_HUB_OFFLINE"] = "1"
    import torch
    from transformers import AutoModelForSeq2SeqLM, AutoTokenizer

    torch.set_num_threads(max(1, min(threads, 8)))
    torch.set_num_interop_threads(1)
    log(f"loading tokenizer from {model_path}")
    tokenizer = AutoTokenizer.from_pretrained(model_path, local_files_only=True)
    log("loading NLLB weights on CPU")
    model = AutoModelForSeq2SeqLM.from_pretrained(
        model_path, local_files_only=True, torch_dtype=torch.float32, low_cpu_mem_usage=True
    )
    model.eval()
    log("applying dynamic INT8 quantization to Linear layers")
    try:
        model = torch.ao.quantization.quantize_dynamic(model, {torch.nn.Linear}, dtype=torch.qint8, inplace=False)
    except Exception:
        # PyTorch 2.8+ also exposes the compatibility namespace below.
        model = torch.quantization.quantize_dynamic(model, {torch.nn.Linear}, dtype=torch.qint8, inplace=False)
    model.eval()
    return torch, tokenizer, model


def translate(torch, tokenizer, model, request):
    tokenizer.src_lang = request["sourceLanguage"]
    encoded = tokenizer(request["text"], return_tensors="pt", truncation=True, max_length=512)
    count = max(1, min(int(request.get("candidateCount", 1)), 4))
    beams = max(count, min(int(request.get("beamWidth", 4)), 8))
    target_id = tokenizer.convert_tokens_to_ids(request["targetLanguage"])
    with torch.inference_mode():
        generated = model.generate(
            **encoded,
            forced_bos_token_id=target_id,
            num_beams=beams,
            num_return_sequences=count,
            max_new_tokens=max(16, min(int(request.get("maxNewTokens", 256)), 512)),
            early_stopping=True,
            return_dict_in_generate=True,
            output_scores=True,
        )
    texts = tokenizer.batch_decode(generated.sequences, skip_special_tokens=True)
    scores = getattr(generated, "sequences_scores", None)
    seen = set()
    candidates = []
    for index, text in enumerate(texts):
        normalized = text.strip()
        if not normalized or normalized in seen:
            continue
        seen.add(normalized)
        score = float(scores[index].item()) if scores is not None else None
        candidates.append({"text": normalized, "score": score})
    return candidates


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--threads", type=int, default=4)
    parser.add_argument("--self-test-echo", action="store_true", help=argparse.SUPPRESS)
    args = parser.parse_args()
    if args.self_test_echo:
        torch = tokenizer = model = None
        emit({"type": "ready", "runtime": "self-test", "quantization": "none", "device": "cpu"})
    else:
        try:
            torch, tokenizer, model = load_runtime(args.model, args.threads)
            emit({"type": "ready", "runtime": "pytorch", "quantization": "dynamic-int8", "device": "cpu"})
        except Exception as exc:
            log(traceback.format_exc())
            emit({"type": "error", "message": f"{type(exc).__name__}: {exc}"})
            return 2

    first_input = True
    for raw_line in sys.stdin.buffer:
        request = None
        try:
            # A correct client sends plain UTF-8. Accept one leading BOM defensively
            # so older clients can still recover without restarting repeatedly.
            if first_input:
                raw_line = raw_line.removeprefix(b"\xef\xbb\xbf")
                first_input = False
            line = raw_line.decode("utf-8")
            request = json.loads(line)
            if request.get("type") != "translate":
                raise ValueError("unsupported request type")
            started = time.perf_counter()
            candidates = ([{"text": request["text"], "score": 0.0}] if args.self_test_echo
                          else translate(torch, tokenizer, model, request))
            emit({"type": "result", "id": request["id"], "candidates": candidates,
                  "elapsedMs": round((time.perf_counter() - started) * 1000)})
        except Exception as exc:
            log(traceback.format_exc())
            emit({"type": "error", "id": request.get("id") if isinstance(request, dict) else None,
                  "message": f"{type(exc).__name__}: {exc}"})
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
