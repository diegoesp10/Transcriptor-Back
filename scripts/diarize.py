"""Offline Community-1 diarization. Never print media, paths, credentials or tracebacks."""
import argparse
import json
import os
from pathlib import Path
import sys
import wave

os.environ["HF_HUB_DISABLE_TELEMETRY"] = "1"
os.environ["PYANNOTE_METRICS_ENABLED"] = "0"
os.environ["HF_HUB_OFFLINE"] = "1"
cache = Path(__file__).resolve().parent.parent / "tools" / "diarization" / "cache"
os.environ.setdefault("MPLCONFIGDIR", str(cache / "matplotlib"))
os.environ.setdefault("HF_HOME", str(cache / "huggingface"))
os.environ.setdefault("TORCH_HOME", str(cache / "torch"))


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--model", required=True)
    parser.add_argument("--check", action="store_true")
    parser.add_argument("--audio")
    parser.add_argument("--output")
    parser.add_argument("--device", choices=["cpu", "cuda"], default="cpu")
    parser.add_argument("--min-speakers", type=int, default=1)
    parser.add_argument("--max-speakers", type=int, default=20)
    args = parser.parse_args()
    try:
        import numpy as np
        import torch
        from pyannote.audio import Pipeline
    except Exception:
        return 10
    model = Path(args.model).resolve()
    if not (model / "config.yaml").is_file():
        return 11
    try:
        torch.set_num_threads(8)
        pipeline = Pipeline.from_pretrained(str(model))
        if pipeline is None:
            return 11
        pipeline.to(torch.device(args.device))
    except Exception:
        return 11
    if args.check:
        return 0
    try:
        if not args.audio or not args.output:
            return 12
        # FFmpeg supplies PCM16 mono/16k. In-memory input avoids torchcodec/shared-FFmpeg decoding.
        with wave.open(args.audio, "rb") as audio:
            if audio.getnchannels() != 1 or audio.getsampwidth() != 2 or audio.getframerate() != 16000:
                return 12
            samples = np.frombuffer(audio.readframes(audio.getnframes()), dtype="<i2").astype(np.float32) / 32768.0
        output = pipeline({"waveform": torch.from_numpy(samples).unsqueeze(0), "sample_rate": 16000},
                          min_speakers=args.min_speakers, max_speakers=args.max_speakers)
        labels = {}
        turns = []
        # Exclusive turns simplify attribution. Labels stay stable for the complete meeting.
        for turn, _, speaker in output.exclusive_speaker_diarization.itertracks(yield_label=True):
            if speaker not in labels:
                labels[speaker] = f"Hablante {len(labels) + 1}"
            turns.append({"startSeconds": turn.start, "endSeconds": turn.end, "speaker": labels[speaker]})
        destination = Path(args.output)
        temporary = destination.with_suffix(".tmp")
        temporary.write_text(json.dumps(turns, ensure_ascii=False, allow_nan=False), encoding="utf-8")
        temporary.replace(destination)
        return 0
    except Exception:
        return 12


if __name__ == "__main__":
    sys.exit(main())
